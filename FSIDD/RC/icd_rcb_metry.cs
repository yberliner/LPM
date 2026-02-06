using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TUInt32 = System.UInt32;

namespace MSGS
{
    public static class RBC_CONSTANTS
    {
        public const int NUM_RCB_METRY_HANDLERS = 14;
        public const int NUM_RCC_METRY_HANDLERS = 10;
        public const int RCB_NUM_FPGA_REGS = 0x25;
    }

    

    public enum GeneralConstants
    {
        E_NUM_RCC = 3,                   // Number of RCCs
        E_NUM_GEN_DEVICES_PER_BOARD = 4, // 4 devices per board for Power/temperature
        E_NUM_IMU_ITEMS = 6,             // Number of IMU items
        E_NUM_ERROR_COUNTERS = 20        // Number of error counters
    }

    public enum Mct8316zDriverRegs
    {
        E_MCT8316Z_DRIVER_REG_START = 0,
        E_MCT8316Z_DRIVER_REG_IC_STAT = E_MCT8316Z_DRIVER_REG_START,
        E_MCT8316Z_DRIVER_REG_STAT_1 = 1,
        E_MCT8316Z_DRIVER_REG_STAT_2 = 2,
        E_MCT8316Z_DRIVER_NUM_OF_STATUS_REGS = 3
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cContinuousBoardData
    {
        public ushort u16EchoCounter;
        public uint u32BITindications;
        public byte u8State;
        public byte u8MaxTemperature;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] u16VoltageSample;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public ushort[] u16Timing;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public byte[] u8ErrorCounters;

        // Simulated union: 8 bytes buffer
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] unionData;

        // Constructor
        public cContinuousBoardData()
        {
            u16EchoCounter = 0;
            u32BITindications = 0;
            u8State = 0;
            u8MaxTemperature = 0;

            u16VoltageSample = new ushort[5];
            u16Timing = new ushort[10];
            u8ErrorCounters = new byte[10];
            unionData = new byte[8];
        }

        // --- sEebPeriph properties ---
        public byte u8Buttons
        {
            get => unionData[0];
            set => unionData[0] = value;
        }

        public byte u8LimitersEEB
        {
            get => unionData[1];
            set => unionData[1] = value;
        }

        public short[] i16RawHallEEB
        {
            get => new short[]
            {
            BitConverter.ToInt16(unionData, 2),
            BitConverter.ToInt16(unionData, 4),
            BitConverter.ToInt16(unionData, 6)
            };
            set
            {
                if (value == null || value.Length != 3)
                    throw new ArgumentException("i16RawHallEEB must have 3 elements");

                Buffer.BlockCopy(BitConverter.GetBytes(value[0]), 0, unionData, 2, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(value[1]), 0, unionData, 4, 2);
                Buffer.BlockCopy(BitConverter.GetBytes(value[2]), 0, unionData, 6, 2);
            }
        }

        // --- sCrashReport properties ---
        public ushort u16Address
        {
            get => BitConverter.ToUInt16(unionData, 0);
            set => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, unionData, 0, 2);
        }

        public ushort u16HelpReg
        {
            get => BitConverter.ToUInt16(unionData, 2);
            set => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, unionData, 2, 2);
        }

        public uint u32Cfsr
        {
            get => BitConverter.ToUInt32(unionData, 4);
            set => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, unionData, 4, 4);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Encoder
    {
        public int i32Raw;
        public float f32Phys;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PIDDebugData
    {
        public float r32Input;
        public float r32Output;
        public float r32Dbg1;
        public short i16Pwm;
        public ushort u16Spare;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cContinuousAxisData
    {
        public uint u32BITindications;

        public ushort u16ILimit;
        public ushort u16BrakeCurrentLoad;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public Encoder[] Encoders;

        public PIDDebugData sPIDDebugData;

        public byte u8ControlAlgorithmType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] u8DriverRegisters;

        public byte u8Temperature;
        public sbyte i8Pwm;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] u8SparesAlign;

        // Constructor to initialize arrays
        public cContinuousAxisData()
        {
            u32BITindications = 0;
            u16ILimit = 0;
            u16BrakeCurrentLoad = 0;

            Encoders = new Encoder[2];
            //Encoders[0] = new Encoder();
            //Encoders[1] = new Encoder();

            sPIDDebugData = new PIDDebugData();

            u8ControlAlgorithmType = 0;
            u8DriverRegisters = new byte[3];
            u8Temperature = 0;
            i8Pwm = 0;
            u8SparesAlign = new byte[2];
        }

        
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SAxisMetryData
    {
        public uint u32BitIndications;
        public uint u32BitStatus;

        public cDigitalSample sCurrentSample;

        //This is the brakes values!
        public cDigitalSample sBrakeCurrentLoad;

        public float r32EncPrimary;
        public float r32EncSecondary;

        public int i32EncPrimary;
        public int i32EncSecondary;

        public float r32Target;
        public float r32Speed;

        public cDigitalSample r32EncoderDiff;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public SPidDebugValues[] asPidDebugValues;

        public byte u8Temperature;
        public byte u8ControlAlgorithmType;
        public byte u8EncoderStatus;
        public byte u8MotorStatus;

        // Constructor: only initialize the array
        public SAxisMetryData()
        {
            asPidDebugValues = new SPidDebugValues[2];
        }
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SBoardMetryData
    {
        public uint u32timeTag;         // Operational channel read time in uSec
        public uint u32DiagCounter;     // Diagnostic counter
        public ushort u16EchoCounter;   // Diagnostic echo counter

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = RBC_CONSTANTS.NUM_RCC_METRY_HANDLERS)]
        public ushort[] u16HandlerDuration; // timing per handler

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_ERROR_COUNTERS)]
        public byte[] u8ErrorCounters;      // error counters

        public uint u32BitIndications; // active indications
        public uint u32BitStatus;      // BIT status for board

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_GEN_DEVICES_PER_BOARD)]
        public cDigitalSample[] u16PowerSample;  // Power physical value

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_GEN_DEVICES_PER_BOARD)]
        public cDigitalSample[] u16TempSample;   // Temperature physical value

        // Constructor: only initialize arrays
        public SBoardMetryData()
        {
            u16HandlerDuration = new ushort[(int)RBC_CONSTANTS.NUM_RCC_METRY_HANDLERS];
            u8ErrorCounters = new byte[(int)GeneralConstants.E_NUM_ERROR_COUNTERS];
            u16PowerSample = new cDigitalSample[(int)GeneralConstants.E_NUM_GEN_DEVICES_PER_BOARD];
            u16TempSample = new cDigitalSample[(int)GeneralConstants.E_NUM_GEN_DEVICES_PER_BOARD];
        }
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SManipulatorExtraData
    {
        public byte u8ButtonsEeb;       // buttons
        public byte u8M7BLimitersEeb;   // M7 Limiters

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] u16HallEEB;     // hall samples

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_IMU_ITEMS)]
        public float[] ar32ImuData;     // IMU data

        public ulong u64ImuTimeTag;     // IMU time tag

        // Constructor: only initialize arrays
        public SManipulatorExtraData()
        {
            u16HallEEB = new ushort[3];
            ar32ImuData = new float[(int)GeneralConstants.E_NUM_IMU_ITEMS];
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PowerStatusBlock
    {
        public cDigitalSample sPower35v;
        public cDigitalSample sPower24v;
        public cDigitalSample sPower12v;
        public cDigitalSample sPower12vMonitor;
        public cDigitalSample sPower3_3v;
        public cDigitalSample sPower5v;
        public cDigitalSample sPower2_5v;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RcbMetryBlock
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)RBC_CONSTANTS.NUM_RCB_METRY_HANDLERS)]
        public ushort[] u16HandlerCycleTime;

        public uint u32FlaStatus;

        public PowerStatusBlock sPowerStatus;

        public byte u8Spare1;
        public byte u8Spare2;
        public uint u32Spare3;

        public uint u32CbitRcb;
        public uint u32CbitRksTarget;
        public uint u32CbitManipulator_Left;
        public uint u32CbitManipulator_Right;

        public RcbMetryBlock()
        {
            u16HandlerCycleTime = new ushort[(int)RBC_CONSTANTS.NUM_RCB_METRY_HANDLERS];
        }

    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BoardStatistics
    {
        public uint u32MsgCounters;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public ushort[] u32ErrorCounters;

        public BoardStatistics()
        {
            u32ErrorCounters = new ushort[10];
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct CommErrorUnion
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] u32CommErrors;

        public CommErrorUnion()
        {
            u32CommErrors = new ushort[5];
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ManipulatorMetryBlock
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_RCC)]
        public cContinuousBoardData[] asBoardData;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMotorIndex.eNumOfMotors)]
        public cContinuousAxisData[] asAxisMetryData;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_RCC)]
        public BoardStatistics[] asBoardStatistcs;

        public CommErrorUnion CommErrors;

        public ManipulatorMetryBlock()
        {
            asBoardData = new cContinuousBoardData[(int)GeneralConstants.E_NUM_RCC];

            for (int i = 0; i < asBoardData.Length; i++)
                asBoardData[i] = new cContinuousBoardData(); // Calls your constructor

            asAxisMetryData = new cContinuousAxisData[(int)eMotorIndex.eNumOfMotors];
            for (int i = 0; i < asAxisMetryData.Length; i++)
                asAxisMetryData[i] = new cContinuousAxisData(); // Calls your constructor

            asBoardStatistcs = new BoardStatistics[(int)GeneralConstants.E_NUM_RCC];
        } 
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FpgaRegsBlock
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)RBC_CONSTANTS.RCB_NUM_FPGA_REGS)]
        public uint[] au32Data;

        public FpgaRegsBlock()
        {
            au32Data = new uint[(int)RBC_CONSTANTS.RCB_NUM_FPGA_REGS];
        }
    }


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SRcControlMetry
    {
        public cheader sHeader;
        public ulong u64timeTag;

        public RcbMetryBlock sRcbMetry;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public ManipulatorMetryBlock[] asManipulatorMetry;

        public FpgaRegsBlock sFpgaRegs;

        public uint u32Checksum;

        public SRcControlMetry()
        {
            asManipulatorMetry = new ManipulatorMetryBlock[(int)e_sides.eNumOfSides];
            for (int i = 0; i < asManipulatorMetry.Length; i++)
                asManipulatorMetry[i] = new ManipulatorMetryBlock();
        }

    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SideMetry
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_RCC)]
        public uint[] u32InitRxCounters;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_RCC)]
        public uint[] u32ControlRxCounters;

        //TODO: FIX THIS FIND SInitBoardData!!!!!!!!!!!!!!!!!!!!!!
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)GeneralConstants.E_NUM_RCC)]
        public SInitBoardData[] asBoards;

        //TODO: FIX THIS FIND SInitAxisData!!!!!!!!!!!!!!!!!!!!!!
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMotorIndex.eNumOfMotors)]
        public SInitAxisData[] asAxes;

        public SideMetry()
        {
            u32InitRxCounters = new uint[(int)GeneralConstants.E_NUM_RCC];
            u32ControlRxCounters = new uint[(int)GeneralConstants.E_NUM_RCC];
            asBoards = new SInitBoardData[(int)GeneralConstants.E_NUM_RCC];
            asAxes = new SInitAxisData[(int)eMotorIndex.eNumOfMotors];
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SRcDebugMetry
    {
        public cheader sHeader;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public SideMetry[] asSide;

        public uint u32Checksum;

        public SRcDebugMetry()
        {
            asSide = new SideMetry[(int)e_sides.eNumOfSides];
            for (int i = 0; i < (int)e_sides.eNumOfSides; i++)
                asSide[i] = new SideMetry();
        }

        
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SInitBoardData
    {
        // Fill in fields as defined in RcInternalMsgs::FpgaDiagProto::SInitBoardData
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SInitAxisData
    {
        // Fill in fields as defined in RcInternalMsgs::FpgaDiagProto::SInitAxisData
    }
}
