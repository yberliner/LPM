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
    public static class MCMetryConstants
    {
        public const int MCC_METRY_IDD_VERSION_MAJOR = (3);
        public const int MCC_METRY_IDD_VERSION_MINOR = (0);
        public const int MCC_METRY_IDD_VERSION_PATCH = (1);

        // The const values here are duplicated from thew project files
        // It's developer's responsibility to keep them in sync
        // Their update requires PLR tables update
        public const int NUM_OF_TIMED_HANDLERS = 16;
        public const int POS_NUM_SENSOR = 6;
        public const int ORI_NUM_SENSOR = 3;
        public const int NUM_ENCODERS_PER_SIDE = 9;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SEncoderDiagData
    {
        public uint u32rawEncoderData;
        public uint u32status;
        public float r32calibratedDegrees;
        public float r32filteredDegrees;
        public uint bCommError;   // 1 if there is a communication error
        public uint u32Spare;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMotorDiagData
    {
        public float r32requestedForce;
        public float r32calculatedPwm;
        public int i32motorCommand;
        public float r32mesuredVoltage;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SMcFastDiagnostics
    {
        public cHeader header;
        public ulong u64TimeTag;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MCMetryConstants.NUM_OF_TIMED_HANDLERS)]
        public uint[] au32LastTaskDuration;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MCMetryConstants.NUM_OF_TIMED_HANDLERS)]
        public uint[] au32MeanTaskDuration;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MCMetryConstants.NUM_OF_TIMED_HANDLERS)]
        public uint[] au32MinTaskDuration;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MCMetryConstants.NUM_OF_TIMED_HANDLERS)]
        public uint[] au32MaxTaskDuration;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * MCMetryConstants.NUM_ENCODERS_PER_SIDE)]
        public SEncoderDiagData[] asEncoderDiagDataArr;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * (int)McStickMotorIndex.StickNumOfMotors)]
        public SMotorDiagData[] asMotorDiagDataArr;

        public uint checksum;

        // Constructor: only initialize arrays
        public SMcFastDiagnostics()
        {
            au32LastTaskDuration = new uint[MCMetryConstants.NUM_OF_TIMED_HANDLERS];
            au32MeanTaskDuration = new uint[MCMetryConstants.NUM_OF_TIMED_HANDLERS];
            au32MinTaskDuration = new uint[MCMetryConstants.NUM_OF_TIMED_HANDLERS];
            au32MaxTaskDuration = new uint[MCMetryConstants.NUM_OF_TIMED_HANDLERS];

            asEncoderDiagDataArr = new SEncoderDiagData[(int)e_sides.eNumOfSides * MCMetryConstants.NUM_ENCODERS_PER_SIDE];
            asMotorDiagDataArr = new SMotorDiagData[(int)e_sides.eNumOfSides * (int)McStickMotorIndex.StickNumOfMotors];
        }
    }

}
