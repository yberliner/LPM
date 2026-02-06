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
    //constexpr uint32_t MOCB_METRY_IDD_VERSION_MAJOR = 1;
    //constexpr uint32_t MOCB_METRY_IDD_VERSION_MINOR = 4;
    //constexpr uint32_t MOCB_METRY_IDD_VERSION_PATCH = 0;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SBitHistory
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public ushort[] u16FailureNumber; // As in Excel column Number (one per row)

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] u8UnitId;           // UnitId

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] u8SubTestId;        // Sub Test index

        public byte u8Index;              // number of items in history
        public byte u8OriginIndex;        // index of the origin (fatal)

        // Constructor to initialize all arrays
        public SBitHistory()
        {
            u16FailureNumber = new ushort[4];
            u8UnitId = new byte[4];
            u8SubTestId = new byte[4];
            u8Index = 0;
            u8OriginIndex = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMocBMetryMsg
    {
        public cHeader header;
        public uint u32timeTag;
        public uint u32DeviceCmdLedCoax1;
        public uint u32DeviceCmdLedCoax2;
        public uint u32DeviceCmdLedParax;
        public uint u32DeviceCmdLedTracking;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
        public ushort[] u16HandlerCycleTime;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public ushort[] u16spareHandlerCycleTime;

        public byte u8Spare4;
        public byte nFaultRollMotor;
        public uint u32Exp0;
        public uint u32Exp1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_mocb_bit_units.E_MOCB_NUM_BIT_UNITS)]
        public BitFieldType[] cbit_results;

        public float r32RollSignedFreq;
        public uint u32_spare6;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] r32spare;

        public float r32Voltage24V;
        public float r32Voltage12V;
        public float r32Voltage5V;
        public float r32Voltage3V3;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES)]
        public eModuleState[] error_state;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] u8spare;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES)]
        public eSysState[] subsystem_state;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] u8Spare_align;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] r32spare2;

        public ushort u16CurrentSensorCoax1_mA;
        public ushort u16CurrentSensorCoax2_mA;
        public ushort u16CurrentSensorParax_mA;
        public ushort u16CurrentSensorTracking_mA;
        public uint u32CalibState;
        public uint u32CurrentSensorCoax1;
        public uint u32CurrentSensorCoax2;
        public uint u32CurrentSensorParax;
        public uint u32CurrentSensorTracking;
        public uint u32Voltage24VRaw;
        public uint u32Voltage12VRaw;
        public uint u32Voltage5VRaw;
        public uint u32Voltage3V3Raw;
        public byte u8EstopStatus;
        public byte u8EstopResetN;
        public byte u8RollMotorStatus;
        public byte u8Spare1;
        public uint u32ChuNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] u32Spare3;

        public SBitHistory sSysHistory;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] u8Spare2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] u32Spare5;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_mocb_bit_units.E_MOCB_NUM_BIT_UNITS)]
        public SBitHistory[] sSubModuleHistory;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] u32Spare6;

        public uint u32OpcodeErrors;
        public uint u32LengthErrors;
        public uint u32ChecksumErrors;
        public uint u32CounterMismatchError;
        public uint u32MissedFrames;
        public uint u32MismatchIdd;
        public uint u32MultipleMsgsInSingleCycle;
        public uint u32FieldRangeError;
        public uint u32CriticalFieldRangeError;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 33)]
        public uint[] u32Spare4;

        public uint checksum;

        // Constructor that initializes array fields only
        public SMocBMetryMsg()
        {
            u16HandlerCycleTime = new ushort[9];
            u16spareHandlerCycleTime = new ushort[2];
            cbit_results = new BitFieldType[(int)e_mocb_bit_units.E_MOCB_NUM_BIT_UNITS];
            r32spare = new float[4];
            error_state = new eModuleState[(int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES];
            u8spare = new byte[6];
            subsystem_state = new eSysState[(int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES];
            u8Spare_align = new byte[2];
            r32spare2 = new float[4];
            u32Spare3 = new uint[3];
            u8Spare2 = new byte[2];
            u32Spare5 = new uint[4];
            sSubModuleHistory = new SBitHistory[(int)e_mocb_bit_units.E_MOCB_NUM_BIT_UNITS];
            for (int i = 0; i < sSubModuleHistory.Length; i++)
            {
                sSubModuleHistory[i] = new SBitHistory();
            }

            u32Spare6 = new uint[4];
            u32Spare4 = new uint[33];
        }
    }
    //static_assert(sizeof(SMocBMetryMsg) == 600, "Wrong msg size, Unplanned IDD change");


}
