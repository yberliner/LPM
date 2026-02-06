using MSGS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static MSGS.Utils;
using BitFieldType = System.UInt32;
using BoardVersion = MSGS.cboard_version;    // naming for embedded
using cHeader = MSGS.cheader;                // message header
using cOpcode = System.Byte;
using ESide = MSGS.e_sides;                  // old enum name
using ExpVersion = MSGS.cexpended_version;   // naming for embedded
using SChannelErrors = MSGS.schannel_errors; // naming for embedded
using SwVersion = MSGS.cexpended_version;    // new naming for Embedded
using Version = MSGS.cversion;               // old struct name

namespace MSGS
{
    

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RKS2RC_Init
    {
        public RKS2RC_Init()
        {
            led_intervals = new sLedInterval[(int)eLedIntervalPattern.eNumOfLedIntervalPatterns];
            led_colors = new sRgbColor[(int)eLedColorPattern.eNumOfLedColorPatterns];
            
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string iniPath = Path.Combine(exeDir, "VisionComputer.global.ini");

            LedIniLoader.Load(iniPath, out led_colors, out led_intervals);

            spare2 = new byte[4];
            spare1 = new UInt32[10];

            header.Opcode = (byte)E_OPCODES.OP_MASTER_INIT_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = RC_Constants.VC_RC_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = RC_Constants.VC_RC_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = RC_Constants.VC_RC_IDD_VERSION_PATCH;
            
            operation_state = eOperationMode.eOperationModeNormal;
        }
        //static constexpr cOpcode def_opcode = OP_RKS_RC_INIT;
        //static constexpr const char* name = "Rks2Rc Init";
        //static constexpr uint32_t idd_version[3] = {RKS_RC_IDD_MAJOR, RKS_RC_IDD_MINOR, RKS_RC_IDD_PATC};
        public cHeader header; // message header

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eLedIntervalPattern.eNumOfLedIntervalPatterns)]
        public sLedInterval[] led_intervals;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eLedColorPattern.eNumOfLedColorPatterns)]
        public sRgbColor[] led_colors;          // sRgbColor

        public sRcBitData spares_bit_masking;                                    // bit masking spares
        public eOperationMode operation_state;                                   // operation mode - OPER or SERVICE
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare2;                                                // spares
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public UInt32[] spare1;                                              // spares

        public UInt32 checksum;                                                // message checksum - 32bit addition
    }
    //static_assert(sizeof(RKS2RC_Init) == 244, "Wrong msg size, Unplanned IDD change");

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RC2RKS_Init
    {
        public RC2RKS_Init()
        {
            rcc_version = new sRcVersion[(int)e_sides.eNumOfSides * (int)eRccBoardIds.eRccNumOfManipulatorboards];
            fpga_rcc_version = new Version[(int)e_sides.eNumOfSides];
            pos_low_limit = new cRcPose[(int)e_sides.eNumOfSides];
            pos_high_limit = new cRcPose[(int)e_sides.eNumOfSides];
            error_state = new eModuleState[(int)eRcSubsystems.eRcNumOfSubsystems];
            spares = new byte[2];
            spare1 = new UInt32[8];

            for (int i = 0; i < (int)e_sides.eNumOfSides; i++)
            {
                pos_low_limit[i] = new cRcPose();
                pos_high_limit[i] = new cRcPose();
            }

        }
        //static constexpr cOpcode def_opcode = OP_RC_RKS_INIT;
        //static constexpr const char* name = "Rc2Rks Init";
        //static constexpr uint32_t idd_version[3] = {RKS_RC_IDD_MAJOR, RKS_RC_IDD_MINOR, RKS_RC_IDD_PATCH};

        public cHeader header;                                                    // message header
        public UInt32 echo_counter;                                           // echo counter for last message received


        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * (int)eRccBoardIds.eRccNumOfManipulatorboards)]
        public sRcVersion[] rcc_version; // rcc boards versions

        public sRcVersion rcb_version;                                          // rcb board version

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public Version[] fpga_rcc_version;                           // fpga versions (left and right)

        public Version fpga_rcb_version;                                        // rcbfpga version
        public UInt64 time_tag_from_power_up;                                 // RCB time from power up

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public cRcPose[] pos_low_limit;                                // Lower limits on allowed values for target motors


        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public cRcPose[] pos_high_limit;                               // Upper limits on allowed values for target motors

        public sRcBitData pbit_status;                                          // PBIT status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eRcSubsystems.eRcNumOfSubsystems)]
        public eModuleState[] error_state;                    // error modules state machine

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spares;                                               // spares

        public float rcb_board_version;                                            // RCB hardware version   
        public float rcb_board_id;                                                 // RCB hardware ID

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public UInt32[] spare1;                                             // spares

        public eOperationMode operation_state;                                  // operation mode - OPER or SERVICE
        public UInt32 checksum;                                               // message checksum - 32bit addition
}
//static_assert(sizeof(RC2RKS_Init) == 472, "Wrong msg size, Unplanned IDD change");
}
