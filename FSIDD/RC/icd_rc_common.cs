using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BitFieldType = System.UInt32;
using cOpcode = System.Byte;
using ESide = MSGS.e_sides;                  // old enum name
using cHeader = MSGS.cheader;                // message header
using Version = MSGS.cversion;               // old struct name
using SwVersion = MSGS.cexpended_version;    // new naming for Embedded
using BoardVersion = MSGS.cboard_version;    // naming for embedded
using SChannelErrors = MSGS.schannel_errors; // naming for embedded
using ExpVersion = MSGS.cexpended_version;   // naming for embedded

namespace MSGS
{
    public static class RC_Constants
    {
        public const int VC_RC_IDD_VERSION_MAJOR = 3;
        public const int VC_RC_IDD_VERSION_MINOR = 3;
        public const int VC_RC_IDD_VERSION_PATCH = 1;

        //constexpr byte OP_VC_MOCB_CTRL = OP_MASTER_PERIODIC_COMMAND;
        //constexpr byte OP_MOCB_VC_STATUS = OP_CONTROLLER_PERIODIC_STATUS;

        //constexpr byte OP_VC_MOCB_INIT = OP_MASTER_INIT_COMMAND;
        //constexpr byte OP_MOCB_VC_INIT = OP_CONTROLLER_INIT_STATUS;

        //constexpr byte OP_VC_MOCB_CALIB = OP_MASTER_CALIB_COMMAND;
        //constexpr byte OP_MOCB_VC_CALIB_STATUS = OP_CONTROLLER_CALIB_STATUS;

    }

    public enum eMotorIndex : byte
    {
        eMotorIndexM1 = 0,
        eMotorIndexM2 = 1,
        eMotorIndexM3 = 2,
        eMotorIndexNumLinearMotors = 3,
        eMotorIndexM4 = eMotorIndexNumLinearMotors,
        eMotorIndexM5 = 4,
        eMotorIndexM6 = 5,
        eMotorIndexM7 = 6,
        eMotorIndexPusher = eMotorIndexM7,
        eNumOfMotors = 7
    }

    public enum eRcEstopStatusAdditionalInfo : byte
    {
        eEstopFpgaFault = 0,       // 0 - ok, 1 - error
        eEstopStatus,              // 0 - disconnected, 1 - operational
        eEstopWdNoRst,             // 0 - WD error, 1 - operational
        eEstopFpgaOpenRequestCmd,  // 0 - FPGA no request, 1 - request estop
        eEstopWdPulseCmd,          // 0 - no command, 1 - WD operational
        eEstopRcbEstopRequestMicb  // 0 - request, 1 - no request
    }

    /// <summary>
    /// eRcIbitCmdType - ibit command type
    /// </summary>
    public enum eRcIbitCmdType : uint
    {
        /// <summary>Operation mode</summary>
        eRcIbitCmdTypeNone = 0,

        /// <summary>Run tests</summary>
        eRcIbitCmdActive = 0x12345678
    }

    /// <summary>
    /// eRcIbitSideTests - ibit side tests
    /// SSR1&2 disabled - 0x00000003
    /// </summary>
    public enum eRcIbitSideTests : uint
    {
        eRcIbitSideTestsSsr1None = 0,

        /// <summary>32v SSR1 disable</summary>
        eRcIbitSideTestsSsr1Disable = 1,

        /// <summary>32v SSR2 disable</summary>
        eRcIbitSideTestsSsr2Disable,

        /// <summary>24v SSR1 disable</summary>
        eRcIbitSideTestsSsr3Disable,

        /// <summary>24v SSR2 disable</summary>
        eRcIbitSideTestsSsr4Disable
    }

    /// <summary>
    /// eRcIbitGeneralTests - general ibit tests
    /// </summary>
    public enum eRcIbitGeneralTests : uint
    {
        /// <summary>none</summary>
        eRcIbitGeneralTestseNone = 0
    }

    /// <summary>
    /// eRcIbits - ibit items
    /// </summary>
    public enum eRcIbits : byte
    {
        /// <summary>index for ibit cmd. @see eRcIbitCmdType</summary>
        eRcIbitCmd = 0,

        /// <summary>run tests for left module. @see eRcIbitSideTests</summary>
        eRcIbitTestsToRunLeft,

        /// <summary>run tests for right module. @see eRcIbitSideTests</summary>
        eRcIbitTestsToRunRight,

        /// <summary>run tests for general module. @see eRcIbitGeneralTests</summary>
        eRcIbitTestsToRunGeneral,

        /// <summary>number of items</summary>
        eRcIbitNumOfItems
    }


    public enum eDrapeLockStatus : byte
    {
        eDrapeLockStatusMin = 1,    // min value for drape lock status
        eDrapeLockStatusLocked = 1, // Indication for normal operation
        eDrapeLockStatusOpen = 2,   // Indication for no draping attached
        eDrapeLockStatusMax = 2     // max value for drape lock status
    }

    public enum eRcCommand : byte
    {
        eRcCommandIdle = 2,         // This state allows for updating the Service Flag and operating FLA
        eRcCommandActive = 5,       // This mode allows manipulator movement
        eRcCommandFatal = 7,        // Fatal state, transition only to programming by cmd
        eRcCommandProgramming = 8   // This mode allows for programming the system, terminal state
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cRcPose
    {
        public cRcPose() 
        {
            poseArr = new float[(int)eMotorIndex.eNumOfMotors];
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMotorIndex.eNumOfMotors)]
        public float[] poseArr;

        // Alias group (x, y, z) shares values with (m1, m2, m3)
        public float m1 { get => poseArr[0]; set => poseArr[0] = value; }
        public float m2 { get => poseArr[1]; set => poseArr[1] = value; }
        public float m3 { get => poseArr[2]; set => poseArr[2] = value; }

        public float m4 { get => poseArr[3]; set => poseArr[3] = value; }
        public float m5 { get => poseArr[4]; set => poseArr[4] = value; }
        public float m6 { get => poseArr[5]; set => poseArr[5] = value; }
        public float plunger { get => poseArr[6]; set => poseArr[6] = value; }

        // Helper properties for aliasing x/y/z
        public float x { get => m1; set => m1 = value; }
        public float y { get => m2; set => m2 = value; }
        public float z { get => m3; set => m3 = value; }

        
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SPidDebugValues
    {
        public float input;
        public byte force_p;
        public byte force_i;
        public byte force_d;
        public byte force_limited;
    }

    public enum eToolTypes : byte
    {
        eToolInvalid = 0,
        eToolKeratomeOnePointTwoMm = 1,
        eToolKeratomeTwoPointFourMm = 2,
        eToolSyringeLidocaine = 3,
        eToolSyringeAntibiotics = 4,
        eToolVisionBlue = 5,
        eToolSyringeCohesiveViscoelastic = 6,
        eToolSyringeDispersiveViscoelastic = 7,
        eToolUtrataForceps = 8,
        eToolChopper = 9,
        eToolSyringeHydrodissection = 10,
        eToolCoaxialIA = 11,
        eToolEyeFixator = 12,
        eToolIOLInjector = 13,
        eToolPhaco = 14,
        eToolLRIKnife = 15,
        eToolDiathermicTip = 16,
        eToolEefLeft = 17,
        eToolEefRight = 18,
        eToolDialer = 19,
        eToolSpatula = 20,
        eToolKratzScratcher = 21,
        eToolBssFlat = 22,
        eNumOfTools = 23
    }

    public enum eRcState : byte
    {
        eRcStateInvalid = (byte)eSysState.eInvalid,
        eRcStateInit = (byte)eSysState.eInit,
        eRcStateIdle = (byte)eSysState.eIdle,
        eRcStateInactive = (byte)eSysState.eInactive,
        eRcStateActivating = (byte)eSysState.eRecovery,
        eRcStateActive = (byte)eSysState.eActive,
        eRcStateStopping = (byte)eSysState.eStopping,
        eRcStateFatal = (byte)eSysState.eFault,
        eRcStateProgramming = (byte)eSysState.eProgramming
    }

    public enum eRccBoardIds : byte
    {
        eRcc4mb = 0,
        eRccM5b = 1,
        eRccEeb = 2,
        eRccNumOfManipulatorboards
    }

    public enum eRcRobotLeds : byte
    {
        eRcRobotLedsLeft = 0,
        eRcRobotLedsCenter = 1,
        eRcRobotLedsRight = 2,
        eRcNumOfRobotLedStrips
    }

    public enum eRcOperationMode : byte
    {
        eRcOperationModeSlow = 1,
        eRcOperationModeFast = 10
    }

    public enum eManipulatorImuData : byte
    {
        eManipulatorImuDataX = 0,
        eManipulatorImuDataY = 1,
        eManipulatorImuDataZ = 2,
        eManipulatorImuDataYaw = 3,
        eManipulatorImuDataPitch = 4,
        eManipulatorImuDataRoll = 5,
        eManipulatorNumOfImuItems = 6
    }

    public enum eRcFlaCmd : uint
    {
        eRcFlaCmdNoChange = 0x00000000,
        eRcFlaCmdUp = 0x12345678,
        eRcFlaCmdDown = 0x56781234
    }

    public enum eRcFlaStatus : byte
    {
        eRcFlaStatusUp = 1,
        eRcFlaStatusDown = 7,
        eRcFlaStatusManual = 14,
        eRcFlaStatusInTransition = 240,
        eRcFlaStatusError = 255
    }

    public enum eRcAlgoType : byte
    {
        eRcAlgoTypePositionLoop = 0,
        eRcAlgoTypeVelocityLoop = 1,
        eRcAlgoTypePressureLoop = 2,
        eRcAlgoTypeDirectPower = 3,
        eRcAlgoTypePressureCalib = 4,
        eRcAlgoTypeMaxOper = eRcAlgoTypePressureCalib,
        eRcAlgoTypeInjection1 = 5,
        eRcAlgoTypeInjection2 = 6,
        eRcNumOfAlgoType = 7
    }

    public enum eRcSpecialAlgParams : byte
    {
        eRcSpecialAlgParamNone = 0,
        eRcSpecialAlgParamCheckOnly = 1,
        eRcSpecialAlgParamForceCalib = 2,
        eMAgneticCalibIdle = 30,
        eMAgneticCalibInOpenMode = 31,
        eMAgneticCalibInCloseMode = 32,
        eMAgneticCalibWriteToMemory = 33
    }

    public enum eRcSubsystems : byte
    {
        eRcSubsystemSpare = 0,
        eRcSubsystemFla = 1,
        eRcSubsystemSensors = 2,
        eRcSubsystemRcbSw = 3,
        eRcSubsystemManipulatorLeft = 4,
        eRcSubsystemManipulatorRight = 5,
        eRcNumOfSubsystems
    }

    public enum eRcPlungerButtonState : byte
    {
        eRcPlungerButtonStatePressed = 1,
        eRcPlungerButtonStateReleased = 2
    }

    public enum eRcButtonState : byte
    {
        eRcButtonStatePressed = 1,
        eRcButtonStateReleased = 2,
        eRcButtonStateError = 3
    }

    public enum eRcLimiterBits : byte
    {
        eRcLimiterOptoSwitch1 = 0,
        eRcLimiterOptoSwitch2,
        eRcLimiterReedSwitch_PusherCollapsed,
        eRcLimiterHomingRequest
    }

    public enum eRcLimiterMasks : byte
    {
        eRcLimiterMasksOptoSwitches = (1 << (int)eRcLimiterBits.eRcLimiterOptoSwitch1) | (1 << (int)eRcLimiterBits.eRcLimiterOptoSwitch2),
        eRcLimiterMasksPusherCollapsed = (1 << (int)eRcLimiterBits.eRcLimiterReedSwitch_PusherCollapsed),
        eRcLimiterMasksHomingRequest = (1 << (int)eRcLimiterBits.eRcLimiterHomingRequest)
    }

    public enum eRcToolHolderStatusBits : byte
    {
        eRcToolHolderStatusBitsLeverLock = 0,
        eRcToolHolderStatusBitsHolderIdentified = 1,
        eRcToolHolderStatusBitsHolderClosed = 2,
        eRcToolHolderStatusBitsToolIdentified = 3
    }

    public enum eRcToolHolderStatusMasks : byte
    {
        eRcToolHolderStatusMasksLeverLock = 1 << (int)eRcToolHolderStatusBits.eRcToolHolderStatusBitsLeverLock,
        eRcToolHolderStatusMasksHolderIdentified = 1 << (int)eRcToolHolderStatusBits.eRcToolHolderStatusBitsHolderIdentified,
        eRcToolHolderStatusMasksHolderClosed = 1 << (int)eRcToolHolderStatusBits.eRcToolHolderStatusBitsHolderClosed,
        eRcToolHolderStatusMasksToolIdentified = 1 << (int)eRcToolHolderStatusBits.eRcToolHolderStatusBitsToolIdentified
    }

    public enum eRcPressureCalibStatus : byte
    {
        ePressureCalibStatusInactive = 0,
        ePressureCalibStatusInProgress___1 = 1,
        ePressureCalibStatusInProgress__10 = 10,
        ePressureCalibStatusInProgress__20 = 20,
        ePressureCalibStatusInProgress__30 = 30,
        ePressureCalibStatusInProgress__40 = 40,
        ePressureCalibStatusInProgress__50 = 50,
        ePressureCalibStatusInProgress__60 = 60,
        ePressureCalibStatusInProgress__70 = 70,
        ePressureCalibStatusInProgress__80 = 80,
        ePressureCalibStatusInProgress__90 = 90,
        ePressureCalibStatusInProgress_100 = 100,
        ePressureCalibStatusDoneSuccess = 101,
        ePressureCalibStatusDoneFailure = 202,
        ePressureCalibStatusDoneFailBadFit = 203,
        ePressureCalibStatusDoneFailThreshold = 204,
        ePressureCalibStatusAborted = 205
    }

    public enum eRcUnits : byte
    {
        eRcUnit__General1 = 0,
        eRcUnit__General2 = 1,
        eRcUnit__LeftArm_ = 2,
        eRcUnit__RightArm = 3,
        eRcUnit__Left_4MB = 4,
        eRcUnit__Right4MB = 5,
        eRcUnit__Left_M5B = 6,
        eRcUnit__RightM5B = 7,
        eRcUnit__Left_EEB = 8,
        eRcUnit__RightEEB = 9,
        eRcUnit__Left__M1 = 10,
        eRcUnit__Left__M2 = 11,
        eRcUnit__Left__M3 = 12,
        eRcUnit__Left__M4 = 13,
        eRcUnit__Left__M5 = 14,
        eRcUnit__Left__M6 = 15,
        eRcUnit__Left__M7 = 16,
        eRcUnit__Right_M1 = 17,
        eRcUnit__Right_M2 = 18,
        eRcUnit__Right_M3 = 19,
        eRcUnit__Right_M4 = 20,
        eRcUnit__Right_M5 = 21,
        eRcUnit__Right_M6 = 22,
        eRcUnit__Right_M7 = 23,
        eRcUnit__Sensors_ = 24,
        eRcUnit__Fla_____ = 25,
        eRcNumOfBitIds = 26
    }

    public enum eRcWheelsBits : byte
    {
        eRcWheelsSens1TransportationBit = 0,
        eRcWheelsSens2SurgeryBit,
        eRcWheelsSens3EmergencyBit,
        eRcWheelsSens4LockedBit,
        eRcWheelsSens5LockedBit,
        eRcWheelsSens1ErrorBit,
        eRcWheelsSens2ErrorBit,
        eRcWheelsSens3ErrorBit,
        eRcWheelsSens4ErrorBit,
        eRcWheelsSens5ErrorBit
    }

    public enum eRcWheelsMask : ushort
    {
        eRcWheelsSens1TransportationMask = 1 << (int)eRcWheelsBits.eRcWheelsSens1TransportationBit,
        eRcWheelsSens2SurgeryMask = 1 << (int)eRcWheelsBits.eRcWheelsSens2SurgeryBit,
        eRcWheelsSens3EmergencyMask = 1 << (int)eRcWheelsBits.eRcWheelsSens3EmergencyBit,
        eRcWheelsSens4LockedMask = 1 << (int)eRcWheelsBits.eRcWheelsSens4LockedBit,
        eRcWheelsSens5LockedMask = 1 << (int)eRcWheelsBits.eRcWheelsSens5LockedBit,
        eRcWheelsSens1ErrorMask = 1 << (int)eRcWheelsBits.eRcWheelsSens1ErrorBit,
        eRcWheelsSens2ErrorMask = 1 << (int)eRcWheelsBits.eRcWheelsSens2ErrorBit,
        eRcWheelsSens3ErrorMask = 1 << (int)eRcWheelsBits.eRcWheelsSens3ErrorBit,
        eRcWheelsSens4ErrorMask = 1 << (int)eRcWheelsBits.eRcWheelsSens4ErrorBit,
        eRcWheelsSens5ErrorMask = 1 << (int)eRcWheelsBits.eRcWheelsSens5ErrorBit
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRcBitData
    {
        public sRcBitData()
        {
            bits = new uint[(int)eRcUnits.eRcNumOfBitIds];
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eRcUnits.eRcNumOfBitIds)] // total fields incl. axes arrays
        public uint[] bits;

        // Accessor properties (replace constants with correct indices if needed)
        public uint rcb
        {
            get => bits[0];
            set => bits[0] = value;
        }

        public uint rcb_2
        {
            get => bits[1];
            set => bits[1] = value;
        }

        public uint left_manipulator
        {
            get => bits[2];
            set => bits[2] = value;
        }

        public uint right_manipulator
        {
            get => bits[3];
            set => bits[3] = value;
        }

        public uint left_4mb
        {
            get => bits[4];
            set => bits[4] = value;
        }

        public uint right_4mb
        {
            get => bits[5];
            set => bits[5] = value;
        }

        public uint left_m5b
        {
            get => bits[6];
            set => bits[6] = value;
        }

        public uint right_m5b
        {
            get => bits[7];
            set => bits[7] = value;
        }

        public uint left_eeb
        {
            get => bits[8];
            set => bits[8] = value;
        }

        public uint right_eeb
        {
            get => bits[9];
            set => bits[9] = value;
        }

        public uint[] left_axes
        {
            get
            {
                var result = new uint[(int)eMotorIndex.eNumOfMotors];
                Array.Copy(bits, 10, result, 0, (int)eMotorIndex.eNumOfMotors);
                return result;
            }
            set
            {
                if (value.Length != (int)eMotorIndex.eNumOfMotors) throw new ArgumentException("left_axes must have 7 elements.");
                Array.Copy(value, 0, bits, 10, (int)eMotorIndex.eNumOfMotors);
            }
        }

        public uint[] right_axes
        {
            get
            {
                var result = new uint[(int)eMotorIndex.eNumOfMotors];
                Array.Copy(bits, 17, result, 0, (int)eMotorIndex.eNumOfMotors);
                return result;
            }
            set
            {
                if (value.Length != (int)eMotorIndex.eNumOfMotors) throw new ArgumentException("right_axes must have 7 elements.");
                Array.Copy(value, 0, bits, 17, (int)eMotorIndex.eNumOfMotors);
            }
        }

        public uint sensors
        {
            get => bits[24];
            set => bits[24] = value;
        }

        public uint fla
        {
            get => bits[25];
            set => bits[25] = value;
        }

    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRcVersion
    {
        public SwVersion version;        // software version
        public uint serial_number;       // serial number for the board
        public uint manufacture_date;    // epoch in seconds since 2020.01.01
        public uint checksum;            // version checksum - 32bit addition
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRcImuData
    {
        public sRcImuData()
        {
            imu_data = new float[(int)eManipulatorImuData.eManipulatorNumOfImuItems];
            spare = new float[4]; // 4 spare floats
        }   

        public ulong imu_time_tag;                           // sample time in RC clock

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eManipulatorImuData.eManipulatorNumOfImuItems)] 
        public float[] imu_data;                      // IMU (X, Y, Z, Yaw, Pitch, Roll)

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] spare;                         // spares
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRcManipulatorCmd
    {
        public sRcManipulatorCmd()
        {
            target = new cRcPose();
            target_velocity = new cRcPose();
            algo_type = new byte[(int)eMotorIndex.eNumOfMotors];
            spares = new uint[4]; // 4 spare uints
        }

        public cRcPose target;
        public cRcPose target_velocity;                       // velocity target
        public eRcOperationMode slow_mode;                    // movement mode
        public byte spare;
        public byte tool_id;
        public byte brake_bitmap;
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMotorIndex.eNumOfMotors)] 
        public byte[] algo_type;                       // should be eRcAlgoType per motor
        
        public byte special_alg_params;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] 
        public uint[] spares;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRcManipulatorStatus
    {
        public sRcManipulatorStatus()
        {
            pose = new cRcPose();
            imu_data = new sRcImuData();
            spare2 = 0;
            spare1 = 0;
        }
        public sRcImuData imu_data;
        public ulong rc_encoders_time_tag;
        public cRcPose pose;
        public byte state;
        public byte tool_id;
        public byte tool_holder;
        public byte drape_lock_status;
        public byte plunger_button_status;
        public byte pusher_limiters;
        public byte pressure_calib_status;
        public byte spare2;
        public uint spare1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRcFlaCmd
    {
        public uint command;
        public uint spare2;
        public uint spare1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRcFlaStatus
    {
        public uint status;
        public uint spare3;
        public uint spare2;
        public uint spare1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRcCalibrationData
    {
        public int offset;
        public int incremental_upper_bound;
        public int incremental_lower_bound;
        public uint absolute_upper_bound;
        public uint absolute_lower_bound;
        public uint absolute_max;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sManipulatorCalibration
    {
        public sManipulatorCalibration()
        {
            m1 = new sRcCalibrationData();
            m2 = new sRcCalibrationData();
            m3 = new sRcCalibrationData();
            m4 = new sRcCalibrationData();
            m5 = new sRcCalibrationData();
            spare2 = new uint[6]; // 6 spare uints
            spare1 = new uint[4]; // 4 spare uints
        }
        public sRcCalibrationData m1;
        public sRcCalibrationData m2;
        public sRcCalibrationData m3;
        public sRcCalibrationData m4;
        public sRcCalibrationData m5;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] spare2;

        public float pusher_active_zone;
        public float pusher_dead_zone;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] spare1;
    }
}
