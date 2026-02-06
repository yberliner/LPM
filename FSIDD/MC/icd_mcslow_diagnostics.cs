using System;
using System.Collections.Generic;
using System.Linq;
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
using ExpVersion = MSGS.cexpended_version;
using System.Runtime.InteropServices;   // naming for embedded

namespace MSGS
{
    public static class MCMetrySlowConstants
    {
        public const int MC_METRY_IDD_VERSION_MAJOR = (3);
        public const int MC_METRY_IDD_VERSION_MINOR = (1);
        public const int MC_METRY_IDD_VERSION_PATCH = (1);

        public const int NUM_OF_SENSOR_DEVICES = 16;

        public const int NUM_OF_TIMED_HANDLERS = 16;
        public const int NUM_OPTIC_SENSORS = 6;
        public const int NUM_MOTORS = (int)McStickMotorIndex.StickNumOfMotors;


    }
    public enum StickEncoderIndex
    {
        E_ENC_INDEX_JOINT_1 = 0,
        E_ENC_INDEX_JOINT_2 = 1,
        E_ENC_INDEX_JOINT_3 = 2,
        E_NUM_OF_MAIN_STICK_ENCODERS = 3,
        E_TIME_SAPMLES_PER_HANDLER = 2
    }

    public enum QuaternionIndex
    {
        E_QUAT_W = 0,
        E_QUAT_I = 1,
        E_QUAT_J = 2,
        E_QUAT_K = 3,
        E_MAX_NUM_OF_QUAT_VALUES = 4
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMcDisgnostics
    {
        public cHeader header;
        public ulong u64TimeTag;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MCMetryConstants.NUM_OF_TIMED_HANDLERS)]
        public uint[] u32TaskDuration;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MCMetryConstants.NUM_OF_TIMED_HANDLERS)]
        public uint[] u32TaskDurationMaximum;

        public eSysState eMcSlowState;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 435)]
        public byte[] au8msgsSpare;

        public uint u32spiExpanderData1;
        public uint u32spiExpanderData2;
        public uint u32spiExpanderData3;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public uint[] au32HapticEnable;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public uint[] au32OpticSensorRefenceValue;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_OPTIC_SENSORS)]
        public uint[] au32OpticSensorsRawValue;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_OPTIC_SENSORS)]
        public uint[] au32OpticSensorCaculatedValue;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_OPTIC_SENSORS)]
        public uint[] au32Spare1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public uint[] au32ForcepsActuationVal;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_MOTORS)]
        public float[] ar32motorsCurrent;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_MOTORS)]
        public uint[] au32Spares2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * MC_Constants.NUM_TRIPPLE_SENSORS)]
        public int[] a32HomingMagneticRawValue;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * (int)McImuEulerAngles.ImuEulerAngles)]
        public float[] ar32IMU;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MCMetrySlowConstants.NUM_OF_SENSOR_DEVICES)]
        public float[] ar32adcSensorsVoltage;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public uint[] ua32Led;

        public ulong u64timeFromPowerUpMilli;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * (int)McImuQuaternionUnits.QuaternionImaginaryParts)]
        public float[] ar32ImuQuaternion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public uint[] u32ImuScheduleStamp;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McFans.NumOfFans)]
        public ushort[] u16FanSpeed;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McFans.NumOfFans)]
        public byte[] u8FanControlCmd;

        public byte u8Spare2;

        public uint checksum;

        // Constructor: only initializes arrays
        public SMcDisgnostics()
        {
            u32TaskDuration = new uint[MCMetryConstants.NUM_OF_TIMED_HANDLERS];
            u32TaskDurationMaximum = new uint[MCMetryConstants.NUM_OF_TIMED_HANDLERS];
            au8msgsSpare = new byte[435];

            au32HapticEnable = new uint[(int)e_sides.eNumOfSides];
            au32OpticSensorRefenceValue = new uint[(int)e_sides.eNumOfSides];
            au32OpticSensorsRawValue = new uint[(int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_OPTIC_SENSORS];
            au32OpticSensorCaculatedValue = new uint[(int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_OPTIC_SENSORS];
            au32Spare1 = new uint[(int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_OPTIC_SENSORS];
            au32ForcepsActuationVal = new uint[(int)e_sides.eNumOfSides];
            ar32motorsCurrent = new float[(int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_MOTORS];
            au32Spares2 = new uint[(int)e_sides.eNumOfSides * MCMetrySlowConstants.NUM_MOTORS];
            a32HomingMagneticRawValue = new int[(int)e_sides.eNumOfSides * MC_Constants.NUM_TRIPPLE_SENSORS];
            ar32IMU = new float[(int)e_sides.eNumOfSides * (int)McImuEulerAngles.ImuEulerAngles];
            ar32adcSensorsVoltage = new float[MCMetrySlowConstants.NUM_OF_SENSOR_DEVICES];
            ua32Led = new uint[(int)e_sides.eNumOfSides];
            ar32ImuQuaternion = new float[(int)e_sides.eNumOfSides * (int)McImuQuaternionUnits.QuaternionImaginaryParts];
            u32ImuScheduleStamp = new uint[(int)e_sides.eNumOfSides];
            u16FanSpeed = new ushort[(int)McFans.NumOfFans];
            u8FanControlCmd = new byte[(int)McFans.NumOfFans];
        }
    }

}
