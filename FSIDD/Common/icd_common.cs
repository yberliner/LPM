using System;
using System.Runtime.InteropServices;
using cOpcode = System.Byte;
#pragma warning disable CS8981

namespace MSGS
{
    public class ICDCommon
    {
        //Compile time check
        const bool LED_PATTERN_CHECK = (byte)eLedIntervalPattern.eNumOfLedIntervalPatterns == (byte)eLedColorPattern.eNumOfLedColorPatterns;

        // This will cause a compilation error if the condition is false
        private static void CompileTimeCheck()
        {
            _ = LED_PATTERN_CHECK ? 0 : throw new System.Exception("LED patterns and colors must be the same (VC)");
        }
    }

    public static class Constants
    {
        public const int SIDE_L = 0;
        public const int SIDE_R = 1;
    }


    public enum e_sides : byte
    {
        eMinSide = 0,
        eLeft = Constants.SIDE_L,
        eRight = Constants.SIDE_R,
        eNumOfSides = 2
    }

    public enum E_OPCODES : byte
    {
        OP_NA = 0,
        OP_MASTER_INIT_COMMAND = 0xA1,
        OP_CONTROLLER_INIT_STATUS = 0xA2,
        OP_MASTER_CONFIG_COMMAND = 0xB1,
        OP_CONTROLLER_CONFIG_STATUS = 0xB2,
        OP_MASTER_CALIB_COMMAND = 0xB3,
        OP_CONTROLLER_CALIB_STATUS = 0xB4,
        OP_MASTER_PERIODIC_COMMAND = 0x05,
        OP_CONTROLLER_PERIODIC_STATUS = 0x06,
        OP_CONTROLLER_METRY_INIT = 0xF0,
        OP_CONTROLLER_METRY_OPER = 0xF1,
        OP_CONTROLLER_METRY_EXTRAS = 0xF2
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cidd_version
    {
        public byte VersionMajor { get; set; }
        public byte VersionMinor { get; set; }
        public byte VersionPatch { get; set; }

        // Constructor accepting an array of 3 integers
        public cidd_version(byte[] versionNumbers)
        {
            if (versionNumbers == null || versionNumbers.Length != 3)
            {
                throw new ArgumentException("Array must contain exactly 3 integers.");
            }

            // Ensure values fit within byte range (0-255)
            VersionMajor = (byte)Math.Clamp(versionNumbers[0], (byte)0, (byte)255);
            VersionMinor = (byte)Math.Clamp(versionNumbers[1], (byte)0, (byte)255);
            VersionPatch = (byte)Math.Clamp(versionNumbers[2], (byte)0, (byte)255);
        }

        public override string ToString()
        {
            return $"{VersionMajor}.{VersionMinor}.{VersionPatch}";
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cheader
    {
        public cOpcode Opcode;
        public cidd_version VersionIdd;
        public uint Counter;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cversion
    {
        public byte VersionMajor;
        public byte VersionMinor;
        public byte VersionRevision;
        public byte VersionBuild;

        public override string ToString()
        {
            return $"{VersionMajor}.{VersionMinor}.{VersionRevision}.{VersionBuild}";
        }
    }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cexpended_version
    {
        public byte VersionMajor;
        public byte VersionMinor;
        public byte VersionRevision;
        public byte VersionBuild;
        public uint GitShaLow;
        public uint GitShaHigh;

        public override string ToString()
        {
            return $"{VersionMajor}.{VersionMinor}.{VersionRevision}.{VersionBuild}";
            //return $"Version: {VersionMajor}.{VersionMinor}.{VersionRevision}.{VersionBuild}, " +
            //       $"Git SHA: {GitShaHigh:X8}{GitShaLow:X8}";
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cboard_version
    {
        public byte VersionMajor;
        public byte VersionMinor;

        public override string ToString()
        {
            return $"{VersionMajor}.{VersionMinor}";
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct schannel_errors
    {
        public uint OpcodeErrors;
        public uint MsgLenErrors;
        public uint ChecksumErrors;
        public uint MissedFrames;
        public uint MultipleMsgsInCycle;
        public uint FieldErrors;
        public uint CounterErrors;
        public uint Spare3;
        public uint Spare2;
        public uint Spare1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sRgbColor
    {
        public byte Red;
        public byte Green;
        public byte Blue;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sLedInterval
    {
        public byte Rising;
        public byte High;
        public byte Falling;
        public byte Low;
        public byte ScaleMs;
    }

    public enum eLedIntervalPattern : byte
    {
        eLedIntervalPattern0 = 0,
        eLedIntervalPattern1 = 1,
        eLedIntervalPattern2 = 2,
        eLedIntervalPattern3 = 3,
        eLedIntervalPattern4 = 4,
        eLedIntervalPattern5 = 5,
        eLedIntervalPattern6 = 6,
        eLedIntervalPattern7 = 7,
        eLedIntervalPattern8 = 8,
        eLedIntervalPattern9 = 9,
        eNumOfLedIntervalPatterns = 10
    }

    public enum eLedColorPattern : byte
    {
        eLedColorPattern0 = 0,
        eLedColorPattern1 = 1,
        eLedColorPattern2 = 2,
        eLedColorPattern3 = 3,
        eLedColorPattern4 = 4,
        eLedColorPattern5 = 5,
        eLedColorPattern6 = 6,
        eLedColorPattern7 = 7,
        eLedColorPattern8 = 8,
        eLedColorPattern9 = 9,
        eNumOfLedColorPatterns = 10
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sLedPattern
    {
        public eLedIntervalPattern LedIntervalPattern;
        public eLedColorPattern LedColorPattern;
    }

    public enum eSysState : byte
    {
        eInvalid = 0,
        eInit,
        eIdle,
        eInactive,
        eRecovery,
        eActive,
        eStopping,
        eFault,
        eProgramming,
        eError,
        eShuttingDown,
        eNumOfStates
    }

    public enum eEstopStateCmd : byte
    {
        eEstopStateNoChange = 1,
        eEstopStateSysDisconnect = 0x99,
        eEstopStateSysOperational = 0xAA
    }

    public enum eModuleState : byte
    {
        eModuleOk = 0,
        eModuleDisabled,
        eModuleError,
        eModuleFatal,
        eNumOfModuleStates
    }

    public enum eOperationMode : uint
    {
        eOperationModeNormal = 0xC001D00D, //Operation
        eOperationModeService = 0xCABEBABE //Service
    }

    public enum eModuleErrorState : byte
    {
        eModuleErrorStateIgnore = 0x0, //open 
        eModuleErrorStateClear = 0x23 //closed
    }

    public enum eEstopSysStatus : byte
    {
        eEstopHwSysDisconnect = 0x66, //open 
        eEstopHwSysOperational = 0xDD //closed
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct cDigitalSample
    {
        public byte u8A0;
        public byte u8B1;

        public float Value()
        {
            return (float)((float)u8A0 + ((float)u8B1 / 100.0));
        }
    }

    public static class DigitalSampleUtils
    {
        public static void FloatToDigitalSample(float value, ref cDigitalSample sample)
        {
            sample.u8A0 = (byte)value;
            sample.u8B1 = (byte)((value - sample.u8A0) * 100);
        }

        public static float DigitalSampleToFloat(cDigitalSample sample)
        {
            return sample.u8A0 + (float)sample.u8B1 / 100.0f;
        }
    }
}
