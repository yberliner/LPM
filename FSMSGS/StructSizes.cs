using MSGS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    // Static handler dictionary
    public static class StructSizes
    {
        public static readonly int MicbPeriodicStatus = Marshal.SizeOf<MicB2VC_Status>();
        public static readonly int MicbInitReply = Marshal.SizeOf<MicB2VC_Init>();
        public static readonly int MicbMetryReply = Marshal.SizeOf<SMicBMetryMsg>();

        public static readonly int MocbPeriodicStatus = Marshal.SizeOf<MocB2VC_Status>();
        public static readonly int MocbInitReply = Marshal.SizeOf<MocB2VC_Init>();
        public static readonly int MocbMetryReply = Marshal.SizeOf<SMocBMetryMsg>();

        public static readonly int RcPeriodicStatus = Marshal.SizeOf<RC2RKS_Status>();
        public static readonly int RcInitReply = Marshal.SizeOf<RC2RKS_Init>();
        public static readonly int RcMetryInitReply = Marshal.SizeOf<SRcDebugMetry>();
        public static readonly int RcMetryOperReply = Marshal.SizeOf<SRcControlMetry>();
        // rc_metry_oper_reply is commented out

        public static readonly int McPeriodicStatus = Marshal.SizeOf<MC2RKS_Status>();
        public static readonly int McInitReply = Marshal.SizeOf<MC2RKS_Init>();
        public static readonly int McMetryFastReply = Marshal.SizeOf<SMcFastDiagnostics>();
        public static readonly int McMetrySlowReply = Marshal.SizeOf<SMcDisgnostics>();

        public static readonly int configStatus = Marshal.SizeOf<sBitConfigStatus>();

        private static readonly Dictionary<string, DevicesScreen> _structToDeviceMap = new()
            {
                { nameof(MicbPeriodicStatus), DevicesScreen.MICB },
                { nameof(MicbInitReply), DevicesScreen.MICB },
                { nameof(MicbMetryReply), DevicesScreen.MICB_METRY },

                { nameof(MocbPeriodicStatus), DevicesScreen.MOCB },
                { nameof(MocbInitReply), DevicesScreen.MOCB },
                { nameof(MocbMetryReply), DevicesScreen.MOCB_METRY },

                { nameof(RcPeriodicStatus), DevicesScreen.RC },
                { nameof(RcInitReply), DevicesScreen.RC },
                { nameof(RcMetryInitReply), DevicesScreen.RC_METRY },

                { nameof(McPeriodicStatus), DevicesScreen.MC_FAST },
                { nameof(McInitReply), DevicesScreen.MC_FAST },
                { nameof(McMetryFastReply), DevicesScreen.MC_FAST_METRY },
                { nameof(McMetrySlowReply), DevicesScreen.MC_SLOW_METRY }
            };

        public static (string StructName, int Size, DevicesScreen Device) GetClosestMatch(byte[] buffer)
        {
            int actualSize = buffer.Length;

            var allSizes = typeof(StructSizes)
                .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                .Where(f => f.FieldType == typeof(int))
                .Select(f => new
                {
                    Name = f.Name,
                    Size = (int)f.GetValue(null)!
                });

            var closest = allSizes
                .OrderBy(entry => Math.Abs(entry.Size - actualSize))
                .FirstOrDefault();

            if (closest == null)
                throw new InvalidOperationException("No size constants found in StructSizes.");

            if (!_structToDeviceMap.TryGetValue(closest.Name, out var device))
                throw new KeyNotFoundException($"No device mapping found for struct: {closest.Name}");

            return (closest.Name, closest.Size, device);
        }


    }
}
