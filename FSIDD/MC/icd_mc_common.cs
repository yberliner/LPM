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
    public static class MC_Constants
    {
        public const int RKS_MC_IDD_VERSION_MAJOR = 3;
        public const int RKS_MC_IDD_VERSION_MINOR = 5;
        public const int RKS_MC_IDD_VERSION_PATCH = 2;

        public const int QUATRNION_IMAGINARY_PARTS = 3;
        public const int NUM_TRIPPLE_SENSORS = 3;

    }

    public enum VibrationMode : byte
    {
        VibrateNone = 0,
        VibrateMode1 = 1,
        VibrateMode2 = 2,
        VibrateMode3 = 3,
        VibrateMode4 = 4,
        VibrateMaxValue = VibrateMode4
    }

    public enum McBoards : byte
    {
        McBoardMcSlow = 0,
        McBoardMcFast = 1,
        McBoardMcSpare = 2,
        McBoardMcNumOfBoards // Automatically gets the next value (3)
    }

    public enum McMonitorCmd : byte
    {
        McMonitorKey = 0,
        McMonitorVal = 1,
        McMonitorCmdNum
    }

    public enum McJoystickLeds : byte
    {
        HandleLedLeft = 0,
        HandleLedRight = 1,
        HomingLedLeft = 2,
        HomingLedRight = 3,
        EstopLed = 4,
        SwsUpDownLed = 5,
        TableUpDownLed = 6,
        BaseLed = 7,
        JoystickNumOfLeds
    }

    public enum EstopMcAdditionalData : byte
    {
        SlowCmdRequestNoEstop = 0,
        FastWatchdogCmdActive = 1,
        FastCmdRequestNoEstop = 2
    }

    public enum McStickMotorIndex : byte
    {
        StickMotorIndexM1 = 0,
        StickMotorIndexM2 = 1,
        StickMotorIndexM3 = 2,
        StickNumOfMotors
    }

    public enum McEloState : byte
    {
        EloDisableMovement = 0,
        EloEnableMovement = 0xAB
    }

    public enum McHomingState : byte
    {
        HomingStateOutside = 1,
        HomingStateInside = 2
    }

    public enum McJoystickJoints : byte
    {
        JoystickJ1 = 0,
        JoystickJ2 = 1,
        JoystickJ3 = 2,
        JoystickJ4 = 3,
        JoystickJ5 = 4,
        JoystickJ6 = 5,
        JoystickNumOfJoints
    }

    public enum McGoldenPose : byte
    {
        GoldenPose1 = 0,
        GoldenPose2 = 1,
        GoldenPose3 = 2,
        NumOfGoldenPoses
    }

    public enum McAxis : byte
    {
        AxisX = 0,
        AxisY = 1,
        AxisZ = 2,
        AxisNumOfPosItems = 3,
        AxisYaw = AxisNumOfPosItems,
        AxisPitch = 4,
        AxisRoll = 5,
        AxisNumOfAllAxes
    }

    public enum McBitUnits : byte
    {
        BitUnitFirst = 0,
        BitUnitJ1Left = BitUnitFirst,
        BitUnitJ2Left,
        BitUnitJ3Left,
        BitUnitJ4Left,
        BitUnitJ5Left,
        BitUnitJ6Left,
        BitUnitJ1Right,
        BitUnitJ2Right,
        BitUnitJ3Right,
        BitUnitJ4Right,
        BitUnitJ5Right,
        BitUnitJ6Right,
        BitUnitHandleLeft,
        BitUnitHandleRight,
        BitUnitSensors,
        BitUnitGeneral,
        BitUnitErgonomics,
        BitUnitSpare1,
        NumOfBitUnits,

        // Treated as Units
        BitUnitModuleJoystickLeft = NumOfBitUnits,
        UnitModuleFirst = BitUnitModuleJoystickLeft,
        BitUnitModuleJoystickRight,
        BitUnitModuleSensors,
        BitUnitModuleGeneral,
        BitUnitModuleErgonomics,
        NumOfBitUnitAndModules
    }

    public enum McBitModules : byte
    {
        BitModulesFirst = 0,
        BitModulesJoystickLeft = BitModulesFirst,
        BitModulesJoystickRight,
        BitModulesSensors,
        BitModulesGeneral,
        BitModulesErgonomics,
        NumOfBitModules
    }

    public enum McHapticState : byte
    {
        HapticStateInvalid = 0,
        HapticStateDisable,
        HapticStateEnable,
        NumOfHapticStates
    }

    public enum McImuQuaternionUnits : byte
    {
        ImuQuatI = 0,
        ImuQuatJ,
        ImuQuatK,
        QuaternionImaginaryParts
    }

    public enum McImuEulerAngles : byte
    {
        ImuYaw = 0,
        ImuPitch,
        ImuRoll,
        ImuEulerAngles
    }

    public enum McFans : byte
    {
        Fan1 = 0,
        Fan2,
        Fan3,
        Fan4,
        Fan5,
        NumOfFans
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sMcPosition
    {
        public float x;
        public float y;
        public float z;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sMcOrientation
    {
        public float q0;
        public float q1;
        public float q2;
        public float q3;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sMcGoldenPose
    {
        public float j1_deg; // joint 1 value in degrees
        public float j2_deg; // joint 2 value in degrees
        public float j3_deg; // joint 3 value in degrees
        public float j4_deg; // joint 4 value in degrees
        public float j5_deg; // joint 5 value in degrees
        public float j6_deg; // joint 6 value in degrees
        public float reserved;
    };

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cMcPose
    {
        public cMcPose()
        {
            poseArr = new float[7];
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public float[] poseArr;

        public float x { get => poseArr[0]; set => poseArr[0] = value; }
        public float y { get => poseArr[1]; set => poseArr[1] = value; }
        public float z { get => poseArr[2]; set => poseArr[2] = value; }

        public float q0 { get => poseArr[3]; set => poseArr[3] = value; }
        public float q1 { get => poseArr[4]; set => poseArr[4] = value; }
        public float q2 { get => poseArr[5]; set => poseArr[5] = value; }
        public float q3 { get => poseArr[6]; set => poseArr[6] = value; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sMcCalibration
    {
        public sMcCalibration()
        {
            golden_pose = new sMcGoldenPose[(int)e_sides.eNumOfSides * (int)McGoldenPose.NumOfGoldenPoses];
            forceps_actuation_min = new ushort[(int)e_sides.eNumOfSides * (int)McJoystickJoints.JoystickNumOfJoints];
            forceps_actuation_max = new ushort[(int)e_sides.eNumOfSides * (int)McJoystickJoints.JoystickNumOfJoints];
            homing_threshold = new uint[(int)e_sides.eNumOfSides];
            pbit_non_critical_masks = new uint[(int)McBitModules.NumOfBitModules];
            pbit_critical_masks = new uint[(int)McBitModules.NumOfBitModules];
            cbit_masks = new uint[(int)McBitModules.NumOfBitModules];
            spare = new uint[30];

        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)e_sides.eNumOfSides * (int)McGoldenPose.NumOfGoldenPoses))]
        public sMcGoldenPose[] golden_pose; // rcc boards versions

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)e_sides.eNumOfSides * (int)McJoystickJoints.JoystickNumOfJoints))]
        public ushort[] forceps_actuation_min;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)e_sides.eNumOfSides * (int)McJoystickJoints.JoystickNumOfJoints))]
        public ushort[] forceps_actuation_max;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)e_sides.eNumOfSides))]
        public uint[] homing_threshold;

        public ulong calibration_epoch;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)McBitModules.NumOfBitModules))]
        public uint[] pbit_non_critical_masks;                // masking for non critical errors

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)McBitModules.NumOfBitModules))]
        public uint[] pbit_critical_masks;                    // masking for critical errors

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)McBitModules.NumOfBitModules))]
        public uint[] cbit_masks;                             // masking for CBIT

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 30)]
        public uint[] spare;                                                  // spares

        public uint checksum;                                                   // message checksum - 32bit addition
    }
    //static_assert(sizeof(sMcCalibration) == 416, "Wrong msg size, Unplanned IDD change");

}
