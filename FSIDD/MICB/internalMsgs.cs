using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using cHeader = MSGS.cheader;                // message header
using ExpVersion = MSGS.cexpended_version;   // naming for embedded

namespace MSGS
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMicBSlowInitCmdMsg
    {
        public cHeader sHeader;
        public ulong u64SystemTimeTag;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbBitModules.eMicbNumOfBitModules)]
        public byte[] u8SubSystemState;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare;

        public uint checksum;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SSlowMicBInitStatusMsg
    {
        public cHeader sHeader;
        public uint u32EchoCounter;
        public uint u32Pbit;
        public byte u8PbitDone;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] u8Spare;

        public byte u8IsCrashed;
        public uint u32BoardVer;
        public uint u32BoardSerialNumber;
        public ExpVersion sMicbSlowVersion;
        public ushort u16Address;
        public ushort u16Bfar;
        public uint u32Cfsr;
        public uint u32LogGitSha;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] u32Spare;

        public uint checksum;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMicbService
    {
        public byte u8Opcode;
        public byte u8Target;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] au8Spare;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] au8Data;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMicBSlowControlMsg
    {
        public cHeader sHeader;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbBitModules.eMicbNumOfBitModules)]
        public byte[] u8SubSystemState;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbFans.eMicbNumOfFans)]
        public byte[] au8FanCmd;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbBitModules.eMicbNumOfBitModules)]
        public eModuleErrorState[] resetBitModuleErrors;

        public SMicbService sService;
        public sRgbColor sDecoLedColor;
        public byte spare2;
        public uint u32Spare;
        public uint checksum;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SSlowMicBStatusMsg
    {
        public cHeader sHeader;
        public ulong u64TimeTag;
        public uint u32EchoCounter;
        public byte u8YaxisStatus;
        public byte u8SignalsFaultStatus;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] u8Spare1;

        public byte u8WheelsStatus;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbBitModules.eMicbNumOfBitModules)]
        public byte[] u8ModuleState;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbBitModules.eMicbNumOfBitModules)]
        public uint[] u32Cbit;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] ar32Spare;

        public SMicbService sService;
        public ushort u16PedalIndications;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public byte[] au8WheelsLockIndication;

        public ushort u16FanRpm1;
        public ushort u16FanRpm2;
        public ushort u16FanRpm3;
        public byte u8RwsSensorsStatus;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] u8Spare;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] u32Spare;

        public uint checksum;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SSlowMicBMetryMsg
    {
        public cHeader sHeader;
        public uint u32TimeTag;
        public uint u32VoltageMeasure24V;
        public uint u32VoltageMeasure12V;
        public uint u32VoltageMeasure5V0;
        public uint u32VoltageMeasure3V3;
        public sbyte i8TemperatureSens1;
        public sbyte i8TemperatureSens2;
        public sbyte i8TemperatureSens3;
        public byte u8spare;
        public int i32SyncReadOffset;
        public uint u32spare1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] spare1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare2;

        public uint u32Exp1;
        public uint u32Exp2;
        public uint u32Exp3;
        public ushort u16Fan1SpeedCountRawData;
        public ushort u16Fan2SpeedCountRawData;
        public ushort u16Fan3SpeedCountRawData;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbBitModules.eMicbNumOfBitModules)]
        public eSysState[] subsystem_state;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public uint[] u32Spare;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] ar32Spare;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public ushort[] u16HandlerCycleTime;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] u16HandlerCycleTimeSpare;

        public uint u32OpcodeErrors;
        public uint u32LengthErrors;
        public uint u32ChecksumErrors;
        public uint u32CounterMismatchError;
        public uint u32MissedFrames;
        public uint u32MultipleMsgsInSingleCycle;
        public uint u32FieldRangeError;
        public uint u32IddErrors;
        public SBitHistory sSysHistory;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] u8spare3;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public SBitHistory[] sSubModuleHistory;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] u8Spare4;

        public uint checksum;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SSlowMicBLogMsg
    {
        public cHeader sHeader;
        public ulong u64TimeTag;
        public ushort u16LogLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] u8spare;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (208 - 4 - 24))] // sizeof(SSlowMicBMetryMsg) - sizeof(uint) - 24
        public byte[] au8Buff;

        public uint checksum;
    }
}
