using MSGS;
using System;
using System.Collections.Generic;
using System.Linq;
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
using System.Runtime.InteropServices;          // Type of CBIT/PBIT fields

namespace MSGS
{
    public class ICDBitConfig
    {
        public const int RKS_CONFIG_IDD_VERSION_MAJOR = 3;
        public const int RKS_CONFIG_IDD_VERSION_MINOR = 0;
        public const int RKS_CONFIG_IDD_VERSION_PATCH = 0;

        private static void CompileTimeCheck()
        {
            //Assert.AreEqual(40, Marshal.SizeOf<sBitConfig>(), "Wrong msg size, Unplanned IDD change");

            //Assert.AreEqual(68, Marshal.SizeOf<sBitConfigStatus>(), "Wrong msg size, Unplanned IDD change");
            //Assert.AreEqual(64, Marshal.SizeOf<sBitConfigControl>(), "Wrong msg size, Unplanned IDD change");

            //static_assert(sizeof(sBitConfigStatus) == 68, "Wrong msg size, Unplanned IDD change");
            //static_assert(sizeof(sBitConfigControl) == 64, "Wrong msg size, Unplanned IDD change");

        }
    }

    /// Terminology and definitions are available from Error Handling Table
    /// @see G:\Shared drives\ForSight Shared Drive\QA\01 DHF\01 Design Inputs\01-9 Error Handling table


    /// @brief This enum represents the BIT config state
    /// 
    public enum eBitSeverity : byte
    {
        eBitOK = eModuleState.eModuleOk,            ///< BiT OK
        eBitLogging = eModuleState.eModuleDisabled, ///< BiT failed triggers logging
        eBitError = eModuleState.eModuleError,      ///< BiT failed triggers error state
        eBitFatal = eModuleState.eModuleFatal       ///< BiT failed triggers fatal state
    }

    public enum eConfigParamType : byte
    {
        eBitConfigParamTypeInvalid = 0, // Invalid value - do not use
        eBitConfigParamTypeUpperThd,    // upper threshold
        eBitConfigParamTypeLowerThd,    // lower threshold
        eBitConfigParamTypeTestValue,   // test value
        eBitConfigParamTypeStuckTime,   // stuck time
        eBitConfigParamTypeSpare2,      // spare
        eBitConfigParamTypeSpare1,      // spare
        eNumOfBitConfigTypes            // NA
    }

    public enum eSubSystemId : byte
    {
        eSubSystemIdRws = 0,// Robotic Workstation
        eSubSystemIdSws,    // Surgery Workstation
        eSubSystemIdMws,    // Microscope Workstation
        eSubSystemIdOther,  // Other
        eNumOfSubSystems,   // Number of systems
        eSubSystemIdInvalid // NA
    }

    public enum eSubSystemId_Service_SW_Only : byte
    {
        eSubSystemIdRws = 0,// Robotic Workstation
        eSubSystemIdSws,    // Surgery Workstation
        eSubSystemIdMws,    // Microscope Workstation
        eSubSystemIdMicB,   // Microscope Base
        eSubSystemIdOther,  // Other
        eNumOfSubSystems,   // Number of systems
        eSubSystemIdInvalid // NA
    }



    /// @brief This struct represents the BIT config
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sBitConfig
    {
       public UInt16 error_id;                  // system error id from Error Handling Table (error number, e.g. 00070)
       public eSubSystemId subsystem_id;        // eSubSystemId - RCS, SWS, MWS, Other
       public byte module_id;                   // Changes per sub-system, e.g. MICB is Sensors, General. MOCB is Sensors, General, TRK, PARAX, COAX L, COAX R.
       public byte unit_id;                     // Unit: Axis 1, Axis 2 ...
       public byte subtest_id;                  // subtest per unit
       public eBitSeverity severity;            // severity
       public byte active;                      // active = 1, inactive = 0
       public eConfigParamType param_type_1; // param 1 type
       public eConfigParamType param_type_2; // param 2 type
       public eConfigParamType param_type_3; // param 3 type
       public eConfigParamType param_type_4; // param 4 type
       public UInt32 spare1;                    // spare
       public float param_1;                 // param 1
       public float param_2;                 // param 2
       public float param_3;                 // param 3
       public float param_4;                 // param 4
       public UInt32 window_size;               // size of window to test
       public UInt32 num_of_errors;             // num of errors in window which raise BIT error
    };
    //static_assert(sizeof(sBitConfig) == 40, "Wrong msg size, Unplanned IDD change");

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sBitConfigControl
    {
        //3 static variables are not sent over UDP
        //const E_OPCODES def_opcode = E_OPCODES.OpMasterConfigCommand;
        //public static string name = "Bit Config Control";
        //public UInt32[] idd_version = {ICDBitConfig.RKS_CONFIG_IDD_VERSION_MAJOR,
        //ICDBitConfig.RKS_CONFIG_IDD_VERSION_MINOR, ICDBitConfig.RKS_CONFIG_IDD_VERSION_PATCH};
        public sBitConfigControl()
        {
            header.Opcode = (byte)E_OPCODES.OP_MASTER_CONFIG_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = ICDBitConfig.RKS_CONFIG_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = ICDBitConfig.RKS_CONFIG_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = ICDBitConfig.RKS_CONFIG_IDD_VERSION_PATCH;

            set_only = 1; // default to set
            spare3 = new byte[11];
            bit_config = new sBitConfig();
            checksum = 0; // default to zero
        }
        public cHeader header;          // message header
        public byte set_only;           // 1 = set, 0 = get

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public byte[] spare3;             // spares

        public sBitConfig bit_config;   // payload
        public UInt32 checksum;         // message checksum - 32bit addition
    };
    //static_assert(sizeof(sBitConfigControl) == 64, "Wrong msg size, Unplanned IDD change");


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sBitConfigStatus
    {
        // 3 first variables are static. Not sent over UDP
        //const E_OPCODES def_opcode = E_OPCODES.OpControllerConfigStatus;
        //const string name = "BIT Config Status";
        //public UInt32[] idd_version = {ICDBitConfig.RKS_CONFIG_IDD_VERSION_MAJOR,
        //    ICDBitConfig.RKS_CONFIG_IDD_VERSION_MINOR, ICDBitConfig.RKS_CONFIG_IDD_VERSION_PATCH};

        public cHeader header;          // message header
        public UInt32 echo_counter;     // echo counter for last message received
        public byte set_ack;            // 1 = set, 0 = get

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public byte[] spare3;             // spares

        public sBitConfig bit_config;   // payload
        public UInt32 checksum;         // message checksum - 32bit addition
    };
    //static_assert(sizeof(sBitConfigStatus) == 68, "Wrong msg size, Unplanned IDD change");
}


