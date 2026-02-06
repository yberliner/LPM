using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using cOpcode = System.Byte;
using ESide = MSGS.e_sides;                  // old enum name
using cHeader = MSGS.cheader;                // message header
using Version = MSGS.cversion;               // old struct name
using SwVersion = MSGS.cexpended_version;    // new naming for Embedded
using BoardVersion = MSGS.cboard_version;    // naming for embedded
using SChannelErrors = MSGS.schannel_errors; // naming for embedded
using ExpVersion = MSGS.cexpended_version;   // naming for embedded
using BitFieldType = System.UInt32;

namespace MSGS
{
    
    //int MICB_METRY_IDD_VERSION_MAJOR = 1;
    //int MICB_METRY_IDD_VERSION_MINOR = 1;
    //int MICB_METRY_IDD_VERSION_PATCH = 0;
    public enum RwsSensorsMapping
    {
        EstopButtonLeftNo1 = 0,   // Bit 0
        EstopButtonLeftNc1 = 1,   // Bit 1
        EstopButtonRightNo1 = 2,  // Bit 2
        EstopButtonRightNc1 = 3,  // Bit 3
        EstopButtonRcbNo1 = 4,    // Bit 4
        EstopButtonRcbNc1 = 5,    // Bit 5
        EstopButtonMcNo1 = 6,     // Bit 6
        EstopButtonMcNc1 = 7      // Bit 7
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMicBMetryMsg
    {
        public cHeader header;
        public ulong u64timeTag;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public ushort[] u16HandlerCycleTime;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] u16spareHandlerCycleTime;

        public uint u32ManagerOpcodeErrorCount;
        public uint u32ManagerChecksumErrorCount;
        public uint u32ManagerLengthErrorCount;
        public uint u32ManagerCounterErrorCount;
        public uint u32ManagerMissedFrameCount;
        public uint u32ManagerMismatchIddCount;
        public uint u32ManagerFieldErrorCount;
        public uint u32ManagerMultipleMsgsInCycleCount;
        public uint u32spare;
        public uint u32HwExp0;
        public byte u8EstopCmdReq_N;
        public byte u8EloCmd;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] u8EstopSpare;

        public uint u32EStopButtonColor;
        public uint u32ledState;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public float[] r32spare;

        public byte u8EStopActivePulseCmd;
        public byte u8EStopActiveCmd;
        public byte u8sapre5;
        public byte u8EstopStatus;
        
        public SMicBSlowControlMsg sSlowControlMsg;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public uint[] u32spare2;

        public SSlowMicBStatusMsg sSlowStatusMsg;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] u32spare3;

        public SSlowMicBMetryMsg sSlowMetryMsg;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] u32spare4;

        public SBitHistory sSysHistory;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] u8spare3;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public SBitHistory[] sSubModuleHistory;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] u8Spare4;

        public uint u32SlowOpcodeErrors;
        public uint u32SlowLengthErrors;
        public uint u32SlowChecksumErrors;
        public uint u32SlowCounterMismatchError;
        public uint u32SlowMissedFrames;
        public uint u32SlowMultipleMsgsInSingleCycle;
        public uint u32SlowFieldRangeError;
        public uint u32SlowIddErrors;
        public uint u32Align;
        public uint checksum;

        // Constructor that initializes only array fields
        public SMicBMetryMsg()
        {
            u16HandlerCycleTime = new ushort[11];
            u16spareHandlerCycleTime = new ushort[3];
            u8EstopSpare = new byte[2];
            r32spare = new float[7];
            u32spare2 = new uint[10];
            u32spare3 = new uint[6];
            u32spare4 = new uint[6];
            u8spare3 = new byte[6];
            sSubModuleHistory = new SBitHistory[2];
            for (int i = 0; i < sSubModuleHistory.Length; i++)
                sSubModuleHistory[i] = new SBitHistory();
            u8Spare4 = new byte[4];
        }
    }




    
}
