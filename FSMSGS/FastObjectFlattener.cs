#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace FS.Optimized
{
    public static class FastObjectFlattener
    {
        // ------------------- Caches -------------------
        private static readonly ConcurrentDictionary<Type, FieldInfo[]> _fieldsCache = new();
        private static readonly ConcurrentDictionary<FieldInfo, Func<object, object?>> _getterCache = new();
        private static readonly ConcurrentDictionary<Type, bool> _isUserTypeCache = new();

        private static FieldInfo[] GetPublicInstanceFields(Type t) =>
            _fieldsCache.GetOrAdd(t, static tt =>
                tt.GetFields(BindingFlags.Public | BindingFlags.Instance));

        private static bool IsUserType(Type t) =>
            _isUserTypeCache.GetOrAdd(t, static tt =>
            {
                if (tt.IsPrimitive || tt.IsEnum) return false;
                var ns = tt.Namespace;
                if (ns is null) return true;
                if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)) return false;
                return true;
            });

        // Fast IL getter that handles both class and struct declaring types safely
        private static Func<object, object?> GetGetter(FieldInfo f) =>
            _getterCache.GetOrAdd(f, static ff =>
            {
                var declType = ff.DeclaringType!;
                var dm = new DynamicMethod(
                    name: $"__get_{declType.FullName}_{ff.Name}",
                    returnType: typeof(object),
                    parameterTypes: new[] { typeof(object) },
                    m: declType.Module,
                    skipVisibility: true);

                var il = dm.GetILGenerator();

                if (declType.IsValueType)
                {
                    // struct owner: unbox-any -> store local -> ldfld via address
                    var tmp = il.DeclareLocal(declType);       // T tmp;
                    il.Emit(OpCodes.Ldarg_0);                  // obj
                    il.Emit(OpCodes.Unbox_Any, declType);      // (T)obj
                    il.Emit(OpCodes.Stloc, tmp);               // tmp = (T)obj
                    il.Emit(OpCodes.Ldloca_S, tmp);            // &tmp
                    il.Emit(OpCodes.Ldfld, ff);                // tmp.Field
                }
                else
                {
                    // class owner: castclass -> ldfld
                    il.Emit(OpCodes.Ldarg_0);                  // obj
                    il.Emit(OpCodes.Castclass, declType);      // (T)obj
                    il.Emit(OpCodes.Ldfld, ff);                // obj.Field
                }

                if (ff.FieldType.IsValueType)
                    il.Emit(OpCodes.Box, ff.FieldType);        // box value-type field

                il.Emit(OpCodes.Ret);

                return (Func<object, object?>)dm.CreateDelegate(typeof(Func<object, object?>));
            });

        // Reference-equality comparer for cycle protection (nullable-correct)
        private sealed class RefEq : IEqualityComparer<object>
        {
            public static readonly RefEq Instance = new();

            // Explicitly 'new' to hide object.Equals(object?)
            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }


        // Lightweight path builder to reduce string allocations
        private ref struct ValueStringBuilder
        {
            private Span<char> _buffer;
            private char[]? _pooled;
            private int _pos;

            public ValueStringBuilder(Span<char> initialBuffer)
            {
                _buffer = initialBuffer;
                _pooled = null;
                _pos = 0;
            }

            public void Append(char c)
            {
                if (_pos >= _buffer.Length) Grow(1);
                _buffer[_pos++] = c;
            }

            public void Append(ReadOnlySpan<char> s)
            {
                if (s.Length == 0) return;
                if (_pos + s.Length > _buffer.Length) Grow(s.Length);
                s.CopyTo(_buffer.Slice(_pos));
                _pos += s.Length;
            }

            public void Append(string s)
            {
                if (!string.IsNullOrEmpty(s)) Append(s.AsSpan());
            }

            public void Clear() => _pos = 0;

            public override string ToString()
            {
                var s = new string(_buffer.Slice(0, _pos));
                _pos = 0; // allow reuse
                return s;
            }

            private void Grow(int needed)
            {
                int newSize = Math.Max((_buffer.Length == 0 ? 16 : _buffer.Length * 2), _pos + needed);
                var newArr = new char[newSize];
                _buffer.Slice(0, _pos).CopyTo(newArr);
                _buffer = _pooled = newArr;
            }
        }

        // ------------------- Public API -------------------
        // Flattens fields to "path" -> value. Returns nulls for missing values.
        public static Dictionary<string, object?> GetNestedPropertyValue<T>(ref T root)
        {
            if (root is null)
                return new Dictionary<string, object?>(StringComparer.Ordinal);

            var work = new ConcurrentQueue<(object obj, Type type, string path)>();
            var visited = new ConcurrentDictionary<object, byte>(RefEq.Instance);

            void Enqueue(object o, string path)
            {
                if (o is null) return;
                if (!visited.TryAdd(o, 0)) return;
                work.Enqueue((o, o.GetType(), path));
            }

            Enqueue(root!, "");

            // Thread-local buffers → no contention on hot path
            var allBuffers = new ConcurrentBag<List<KeyValuePair<string, object?>>>();
            int dop = Math.Max(2, Environment.ProcessorCount);

            Parallel.For(0, dop, _ =>
            {
                var local = new List<KeyValuePair<string, object?>>(4096);
                Span<char> init = stackalloc char[256];
                var sb = new ValueStringBuilder(init);

                while (work.TryDequeue(out var item))
                {
                    var (obj, objType, basePath) = item;

                    if (!IsUserType(objType))
                    {
                        if (basePath.Length != 0)
                            local.Add(new(basePath, obj));
                        continue;
                    }

                    var fields = GetPublicInstanceFields(objType);
                    for (int i = 0; i < fields.Length; i++)
                    {
                        var field = fields[i];

                        // Build "fieldPath"
                        sb.Clear();
                        if (basePath.Length != 0) { sb.Append(basePath); sb.Append('.'); }
                        sb.Append(field.Name);
                        string fieldPath = sb.ToString();

                        object? value;
                        try
                        {
                            value = GetGetter(field)(obj);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException($"Error reading '{fieldPath}'", ex);
                        }

                        if (value is null)
                        {
                            local.Add(new(fieldPath, null));
                            continue;
                        }

                        var fType = field.FieldType;

                        if (fType.IsArray)
                        {
                            if (value is Array arr)
                            {
                                var elemType = fType.GetElementType()!;
                                int len = arr.Length;

                                for (int idx = 0; idx < len; idx++)
                                {
                                    object? el;
                                    try { el = arr.GetValue(idx); }
                                    catch (Exception ex)
                                    {
                                        throw new InvalidOperationException($"Error accessing '{fieldPath}[{idx}]'", ex);
                                    }

                                    sb.Clear();
                                    sb.Append(fieldPath);
                                    sb.Append('[');
                                    sb.Append(idx.ToString());
                                    sb.Append(']');
                                    string elPath = sb.ToString();

                                    if (el is null)
                                    {
                                        local.Add(new(elPath, null));
                                    }
                                    else if (IsUserType(elemType))
                                    {
                                        if (visited.TryAdd(el, 0))
                                            work.Enqueue((el, el.GetType(), elPath));
                                    }
                                    else
                                    {
                                        local.Add(new(elPath, el));
                                    }
                                }
                            }
                            else
                            {
                                // Defensive: type says array, value isn't actually Array
                                local.Add(new(fieldPath, value));
                            }
                        }
                        else if (IsUserType(fType))
                        {
                            if (visited.TryAdd(value, 0))
                                work.Enqueue((value, value.GetType(), fieldPath));
                        }
                        else
                        {
                            local.Add(new(fieldPath, value));
                        }
                    }
                }

                allBuffers.Add(local);
            });

            // Merge thread-local buffers
            int total = 0;
            foreach (var buf in allBuffers) total += buf.Count;

            var result = new Dictionary<string, object?>(total, StringComparer.Ordinal);
            foreach (var buf in allBuffers)
            {
                for (int i = 0; i < buf.Count; i++)
                {
                    var kv = buf[i];
                    result[kv.Key] = kv.Value;
                }
            }

            return result;
        }
    }
}
