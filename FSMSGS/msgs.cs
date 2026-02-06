using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
#nullable disable

namespace MSGS
{
    public enum DevicesScreen : int
    {
        EMULATOR = 1,
        MICB =2, 
        MOCB =3,
        RC  = 4,
        MC_FAST =5,
        MC_SLOW = 6,
        MICB_METRY = 7,
        MOCB_METRY = 8,
        RC_METRY = 9,
        MC_FAST_METRY = 10,
        MC_SLOW_METRY = 11,
        RC_CONFIG = 12,
        MC_FAST_CONFIG = 13,
        MOCB_CONFIG = 14,
        MICB_CONFIG = 15
    }
    public enum Cmd : int
    {
        InitDevice=0,
        Forward,            // Just forward to device
        OneTimeForward,     // just one time msg forward. no loop
        Record,             // Start record
        Stop_Record,        // Stop record
        Register_agent,     // agent is registering at server
        Ping,               // server is sending ping msg (keep alive)
        MessageSentToDevice,// agent sent a msg to device (for time stamp)
        GetVersion,          // get the version. set forward data only if not exists
        LogOFF,
        LogDefaultOn,
        LogCustomOn,
        CloseAgent
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InitDevice
    {
        public int start_stop;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 100)]
        public string ip;

        public int port;
        public int receive_port; //this is the port the deviece will send status to.
        public int delay_time_micro_seconds;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CmdForward
    {
        public int len;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 900)]
        public byte[] buffer;
    }

    /// <summary>
    /// status msgs from the agent per the devices.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ServerMsg
    {
        public int device;
        public int device_type_msg;
        public long timestamp_us; // corresponds to C++ int64_t
        public int msg_sequence;
        public int buffer_max_len;
        public int size_of_data;
        public Cmd cmd;
        public int forward_id;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 50)]
        public byte[] agent_name;

        public int num_of_non_scriptes_msgs;
        public int num_of_scriptes_msgs;

        public byte log_status;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 66)]
        public byte[] spare;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1500)]
        public byte[] buffer;
    }

    /// <summary>
    /// Msgs that are sent to the agent per the devices.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DeviceMsg
    {
        public int device;
        public int cmd;
        public uint data_len;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DeviceInitData
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
        public byte[] IP;

        public uint listen_port;
        public uint out_port;
        public uint micro_sec_delay;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct DeviceForwardData
    {
        public int id;
        public int num_of_msgs;
        public int size_of_compressed_buffer;
        public int numOfTimesToRepeat;
        public int num_of_repeat_first_line; // This emulates the GoTo command.

        //the size of each msg after compression
        public uint[] compressed_size_per_msg;

        //the size of each msg before compression
        public uint[] unCompressed_size_per_msg;

        //a buffer that holds all compressed msgs.
        //Its size is the sum of all compressed_size_per_msg values (same as size_of_compressed_buffer)
        public byte[] compressedBuffer;
    }

    public static class MSGHelper
    {
        public static T UpdateEmbededVersion<T> (T periodic_msg, DevicesScreen device, 
            EMBVersionStorage _emb_versions, string AgentName) where T : struct
        {
            byte[] device_msg = MSGHelper.StructureToByteArray(periodic_msg);

            //get the old version from the msg
            byte[] slice_version = device_msg.Skip(1).Take(3).ToArray();
            cidd_version old_version = new cidd_version(slice_version);

            cidd_version new_version = _emb_versions.GetVersion(AgentName, device, old_version);

            //copy the new version into the struct
            Array.Copy(MSGHelper.StructureToByteArray(new_version), 0, device_msg, 1, 3);

            Console.WriteLine($"[UpdateEmbededVersion] machine: {AgentName}, Device: {device}, Version Updated: {old_version} -> {new_version}");
            return MSGHelper.ByteArrayToStruct<T>(device_msg);
        }

       // private static readonly ThreadLocal<byte[]> threadBuffer = new(() => new byte[2000]);
        private static readonly ThreadLocal<IntPtr> _threadPtr = new(() => Marshal.AllocHGlobal(10_000));
        //private static readonly IntPtr _staticPtr = Marshal.AllocHGlobal(10_000);

        // Converts struct to byte array
        public static byte[] StructureToByteArray<T>(T obj) where T : struct
        {
            int size = Marshal.SizeOf(obj);
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(obj, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        public static T ByteArrayToStruct<T>(byte[] byteArray) where T : struct
        {
            int expectedSize = Marshal.SizeOf<T>();
            if (expectedSize > 10_000)
                throw new InvalidOperationException("Struct size exceeds static buffer capacity.");

            var ptr = _threadPtr.Value;
            if (expectedSize > byteArray.Length)
            {
                //Console.WriteLine("Error in ByteArrayToStruct. expected size is bigger the input byte array");
                expectedSize = byteArray.Length;
            }
            Marshal.Copy(byteArray, 0, ptr, expectedSize);
            return Marshal.PtrToStructure<T>(ptr);
        }
        public static bool IsArrayValidAsMsg<T>(byte[] byteArray)
        {
            return Marshal.SizeOf<T>() == byteArray.Length;
        }



        public static bool IsSocketConnected(TcpClient client)
        {
            try
            {
                if (client == null || !client.Client.Connected)
                    return false;

                // Check if the socket is ready to read
                if (client.Client.Poll(0, SelectMode.SelectRead))
                {
                    byte[] buffer = new byte[1];
                    // Peek data: If the result is 0, connection was closed
                    if (client.Client.Receive(buffer, SocketFlags.Peek) == 0)
                    {
                        CloseTCPConnection(client); // Close the socket if no data is received (connection closed)
                        return false; // Connection closed
                    }
                }
                // If the socket is not ready to read, it's still connected, so we don't close it
                // Instead, you can choose to check if there's any data waiting to be sent or handle other conditions
                // Here, instead of closing immediately, you can just return true.

                // Send a zero-byte packet to force TCP stack validation
                client.Client.Send(Array.Empty<byte>(), SocketFlags.None);

                return true; // Connection is alive
            }
            catch (Exception e)
            {
                Console.WriteLine($"IsSocketConnected exception: {e.ToString()}");
                try
                {
                    client.Client.Close(); // Close the socket on error
                }
                catch (Exception closeEx)
                {
                    Console.WriteLine($"Error IsSocketConnected exception closing socket: {closeEx.ToString()}");
                }
                return false;
            }
        }

        private static bool CloseTCPConnection(TcpClient client)
        {
            try
            {
                if (client != null && client.Connected)
                {
                    client.Client.Shutdown(SocketShutdown.Both);
                    client.Close();
                    return true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error closing TCP connection: {e.Message}");
            }
            return false;
        }
        public static byte[] StringToFixedSizeBytes(string input, int size)
        {
            byte[] buffer = new byte[size];
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);

            int copyLength = Math.Min(inputBytes.Length, size);

            Array.Copy(inputBytes, buffer, copyLength);

            return buffer;
        }
    }
    public class StructWrapper<T> : INotifyPropertyChanged where T : struct
    {
        private T _structData;
        public StructWrapper(T structData)
        {
            _structData = structData;
        }

        public T StructData
        {
            get => _structData;
            set
            {
                _structData = value;
                NotifyAllPropertiesChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void NotifyAllPropertiesChanged()
        {
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                NotifyPropertyChanged(field.Name);
            }
        }

        // Dynamically update from a struct instance
        public void UpdateFromStruct(T structData)
        {
            StructData = structData;
        }

        
    }

    public static class ReflectionHelper
    {

        public static bool IsComplexType(object obj)
        {
            if (obj == null) return false;

            Type type = obj.GetType();
            return type.IsArray ||                      // <-- NEW
                   type.IsClass ||
                   (type.IsValueType && !type.IsPrimitive && !type.IsEnum && type.Namespace != "System");
        }

        private static void AddArrayItems(Array array, string parentName, Dictionary<string, object> result)
        {
            for (int i = 0; i < array.Length; i++)
            {
                object item = array.GetValue(i);
                string itemName = $"{parentName}[{i}]";

                if (item != null && IsComplexType(item))
                {
                    foreach (var kvp in GetPropertiesRecursive(item, itemName))
                        result[kvp.Key] = kvp.Value;
                }
                else
                {
                    result[itemName] = item;
                }
            }
        }


        public static Dictionary<string, object> GetPropertiesRecursive(object obj, string parentName = "")
        {
            var result = new Dictionary<string, object>();

            if (obj == null)
                return result;

            Type type = obj.GetType();

            // Handle Arrays
            if (type.IsArray)
            {
                AddArrayItems((Array)obj, parentName, result);
                return result;
            }

            // Handle Structs
            if (type.IsValueType && !type.IsPrimitive && !type.IsEnum && type.Namespace != "System")
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    object fieldValue = field.GetValue(obj);
                    string fieldName = string.IsNullOrEmpty(parentName) ? field.Name : $"{parentName}.{field.Name}";
                    
                    if (fieldValue is Array arr)
                    {
                        AddArrayItems(arr, fieldName, result);
                    }
                    else if (fieldValue != null && IsComplexType(fieldValue))
                    {
                        var subProperties = GetPropertiesRecursive(fieldValue, fieldName);
                        foreach (var subProp in subProperties)
                        {
                            result[subProp.Key] = subProp.Value;
                        }
                    }
                    else
                    {
                        result[fieldName] = fieldValue;
                    }
                }
            }
            else
            {
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    object fieldValue = field.GetValue(obj);
                    string fieldName = string.IsNullOrEmpty(parentName) ? field.Name : $"{parentName}.{field.Name}";

                    if (fieldValue is Array arr)
                    {
                        AddArrayItems(arr, fieldName, result);
                    }
                    else if (fieldValue != null && IsComplexType(fieldValue))
                    {
                        foreach (var subProp in GetPropertiesRecursive(fieldValue, fieldName))
                            result[subProp.Key] = subProp.Value;
                    }
                    else
                    {
                        result[fieldName] = fieldValue;
                    }
                }
            }

            return result;
        }

        public static void ApplyChangesToStruct<T>(ref T structObject,
                                           Dictionary<string, object> changes)
    where T : struct
        {
            foreach (var change in changes)
            {
                string path = change.Key;      // e.g. "led_patterns.led_robot_decoration[0].LedIntervalPattern"
                object newValue = change.Value;

                object current = structObject;    // box the top-level struct
                var stack = new Stack<(object parent, FieldInfo field, int? index)>(); // remember every hop

                string[] parts = path.Split('.');

                for (int i = 0; i < parts.Length; i++)
                {
                    string segment = parts[i];
                    int? arrayIdx = null;

                    // -------- Handle “[idx]” syntax --------
                    int lb = segment.IndexOf('[');
                    if (lb >= 0)
                    {
                        int rb = segment.IndexOf(']', lb);
                        arrayIdx = int.Parse(segment.Substring(lb + 1, rb - lb - 1));
                        segment = segment.Substring(0, lb);          // field name without the brackets
                    }

                    FieldInfo field = current.GetType()
                                             .GetField(segment, BindingFlags.Public | BindingFlags.Instance);

                    if (field == null)
                        throw new InvalidOperationException($"Field \"{segment}\" not found on {current.GetType()}");

                    bool lastSegment = (i == parts.Length - 1);

                    // -------- Array element --------
                    if (arrayIdx.HasValue)
                    {
                        Array arr = (Array)field.GetValue(current);
                        if (arr == null)
                            throw new InvalidOperationException($"Array \"{segment}\" is null.");

                        if (lastSegment)
                        {
                            arr.SetValue(ConvertTo(arr.GetType().GetElementType(), newValue), arrayIdx.Value);
                            AssignValue(ref current, field, arr);
                        }
                        else
                        {
                            object element = arr.GetValue(arrayIdx.Value);
                            stack.Push((current, field, arrayIdx));    // remember where the element lives
                            current = element;
                        }
                    }
                    // -------- Plain field --------
                    else
                    {
                        if (lastSegment)
                        {
                            AssignValue(ref current, field, ConvertTo(field.FieldType, newValue));
                        }
                        else
                        {
                            stack.Push((current, field, null));
                            current = field.GetValue(current);
                        }
                    }
                }

                // -------- Bubble the changes back up --------
                while (stack.Count > 0)
                {
                    var (parent, field, idx) = stack.Pop();

                    if (idx.HasValue)                                // we’re updating an array element
                    {
                        Array arr = (Array)field.GetValue(parent);
                        arr.SetValue(current, idx.Value);
                        AssignValue(ref parent, field, arr);
                    }
                    else                                             // plain struct field
                    {
                        AssignValue(ref parent, field, current);
                    }

                    current = parent;
                }

                structObject = (T)current;                           // write back to the ref parameter
            }
        }

        /// <summary>
        /// Writes <paramref name="value"/> into <paramref name="field"/> on <paramref name="parent"/>,
        /// handling both class and struct parents correctly.
        /// </summary>
        private static void AssignValue(ref object parent, FieldInfo field, object value)
        {
            if (parent.GetType().IsValueType)
            {
                TypedReference tr = __makeref(parent);
                field.SetValueDirect(tr, value);
            }
            else
            {
                field.SetValue(parent, value);
            }
        }

        /// <summary>
        /// Converts the supplied value to the requested type, with enum support.
        /// </summary>
        private static object ConvertTo(Type targetType, object value)
        {
            if (targetType.IsEnum)
            {
                var underlying = Enum.GetUnderlyingType(targetType);
                var baseVal = Convert.ChangeType(value, underlying);
                return Enum.ToObject(targetType, baseVal);
            }
            return Convert.ChangeType(value, targetType);
        }




        //For ClientManager
        /// <summary>
        /// Sets ANY nested field or array element designated by <paramref name="path"/>.
        /// Works for arbitrarily-deep structs, arrays and reference-types.
        /// </summary>
        
        public static void SetNestedPropertyValue<T>(ref T root, string path, object value)
        {
            try
            {

                if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));

                /* ------------------------------------------------------------------
                   1.  Walk down the path, keeping a stack of (parentObject, fieldInfo)
                       so we can climb back up later and re-assign modified structs.
                -------------------------------------------------------------------*/
                var parts = path.Split('.');
                // track parent, the field (or array field), and (optionally) an array index to write back into
                var stack = new Stack<(object parent, FieldInfo field, int? arrayIndex)>();
                object current = root!;

                for (int i = 0; i < parts.Length; i++)
                {
                    string part = parts[i];

                    // Array element syntax: fieldName[index]
                    int b0 = part.IndexOf('[');
                    int b1 = part.IndexOf(']');
                    bool isArray = b0 >= 0 && b1 > b0;

                    if (isArray)
                    {
                        string fieldName = part.Substring(0, b0);
                        int index = int.Parse(part[(b0 + 1)..b1], CultureInfo.InvariantCulture);

                        FieldInfo arrField = current.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (arrField is null) throw new MissingFieldException(current.GetType().Name, fieldName);

                        Array arr = (Array)arrField.GetValue(current)!;

                        if (i == parts.Length - 1)
                        {
                            // last step – set the array element
                            var elementType = arr.GetType().GetElementType()!;
                            object converted = ConvertTo(value, elementType);
                            arr.SetValue(converted, index);
                            arrField.SetValue(current, arr); // write array back into its parent
                        }
                        else
                        {
                            // dive into the element; remember where to write it back
                            stack.Push((current, arrField, index));
                            current = arr.GetValue(index)!;
                        }
                    }
                    else
                    {
                        FieldInfo field = current.GetType().GetField(part, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (field is null) throw new MissingFieldException(current.GetType().Name, part);

                        if (i == parts.Length - 1)      // *** last step – set element ***
                        {
                            // last step – set the field (supports enums & nullable enums)
                            object converted = ConvertTo(value, field.FieldType);
                            field.SetValue(current, converted);
                        }
                        else
                        {
                            stack.Push((current, field, null)); // remember parent
                            current = field.GetValue(current)!;  // dive deeper
                        }
                    }
                }

                // climb back up: reinsert modified child into each parent (handles arrays too)
                while (stack.Count > 0)
                {
                    var (parent, field, arrayIndex) = stack.Pop();

                    if (arrayIndex.HasValue)
                    {
                        // field holds an array; 'current' is the modified element
                        Array arr = (Array)field.GetValue(parent)!;
                        arr.SetValue(current, arrayIndex.Value);
                        field.SetValue(parent, arr);
                    }
                    else
                    {
                        field.SetValue(parent, current);
                    }

                    current = parent;
                }

                // write the modified top-level object back
                root = (T)current;
            }
            catch (Exception e)
            {
                Console.WriteLine($"SetNestedProperty exception: {e.ToString()}");               
            }
        }

        private static object ConvertTo(object value, Type destinationType)
        {
            // handle nulls (nullable targets only)
            if (value is null)
            {
                if (Nullable.GetUnderlyingType(destinationType) != null) return null;
                throw new InvalidCastException($"Cannot assign null to non-nullable {destinationType}.");
            }

            // unwrap Nullable<T>
            var underlyingNullable = Nullable.GetUnderlyingType(destinationType);
            var targetType = underlyingNullable ?? destinationType;

            // if already assignable, return as-is
            if (targetType.IsInstanceOfType(value)) return value;

            // enums (including nullable enums)
            if (targetType.IsEnum)
            {
                if (value is string s)
                {
                    // allow names; ignore case
                    return Enum.Parse(targetType, s, ignoreCase: true);
                }

                // allow numeric (or other enum) via underlying conversion
                var underlying = Enum.GetUnderlyingType(targetType);
                var baseValue = System.Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture)!;
                return Enum.ToObject(targetType, baseValue);
            }

            // friendly bool from "0"/"1"
            if (targetType == typeof(bool) && value is string sb)
            {
                if (sb == "1") return true;
                if (sb == "0") return false;
            }

            // general conversion
            return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        public static Dictionary<string, object> GetNestedPropertyValue<T>(ref T root)
        {
            try
            {
                var result = new Dictionary<string, object>();
                void Traverse(object current, string path)
                {
                    if (current == null) return;
                    var type = current.GetType();

                    foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                    {
                        var value = field.GetValue(current);
                        string fieldPath = string.IsNullOrEmpty(path) ? field.Name : $"{path}.{field.Name}";

                        if (value == null)
                        {
                            result[fieldPath] = null;
                            continue;
                        }

                        if (field.FieldType.IsArray)
                        {
                            var arr = value as Array;
                            if (arr != null)
                            {
                                for (int i = 0; i < arr.Length; i++)
                                {
                                    var element = arr.GetValue(i);
                                    string arrPath = $"{fieldPath}[{i}]";
                                    if (element != null && !field.FieldType.GetElementType().IsPrimitive && !field.FieldType.GetElementType().IsEnum && field.FieldType.GetElementType().Namespace != "System")
                                    {
                                        Traverse(element, arrPath);
                                    }
                                    else
                                    {
                                        result[arrPath] = element;
                                    }
                                }
                            }
                        }
                        else if (!field.FieldType.IsPrimitive && !field.FieldType.IsEnum && field.FieldType.Namespace != "System")
                        {
                            Traverse(value, fieldPath);
                        }
                        else
                        {
                            result[fieldPath] = value;
                        }
                    }
                }
                Traverse(root, "");
                return result;
            }
            catch (Exception e)
            {
                Console.WriteLine($"GetNestedProperty Exception: {e.ToString()}");
                return new Dictionary<string, object>();
            }
        }
    }
}
