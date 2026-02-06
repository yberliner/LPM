using MSGS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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
    public struct RKS2MC_Init
    {
        public RKS2MC_Init()
        {
            led_intervals = new sLedInterval[(int)eLedIntervalPattern.eNumOfLedIntervalPatterns];
            led_colors = new sRgbColor[(int)eLedColorPattern.eNumOfLedColorPatterns];
            
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string iniPath = Path.Combine(exeDir, "VisionComputer.global.ini");

            Utils.LedIniLoader.Load(iniPath, out led_colors, out led_intervals);

            header.Opcode = (byte)E_OPCODES.OP_MASTER_INIT_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = MC_Constants.RKS_MC_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = MC_Constants.RKS_MC_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = MC_Constants.RKS_MC_IDD_VERSION_PATCH;

            operation_state = eOperationMode.eOperationModeNormal;

            spare2 = new byte[4];
            spare1 = new uint[8];
        }
        //static constexpr cOpcode def_opcode = msgs::OP_RKS_MC_INIT;
        //static constexpr const char* name = "Rks2Mc Init";
        //static constexpr uint idd_version[3] = {RKS_MC_IDD_VERSION_MAJOR, RKS_MC_IDD_VERSION_MINOR, RKS_MC_IDD_VERSION_PATCH};

        public cHeader header;                                        // message header

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eLedIntervalPattern.eNumOfLedIntervalPatterns)]
        public sLedInterval[] led_intervals; // cLedInterval

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eLedIntervalPattern.eNumOfLedIntervalPatterns)]
        public sRgbColor[] led_colors;          // cRgbColor

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare2;                                     // spares

        public eOperationMode operation_state;                        // operation mode - OPER or SERVICE

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] spare1;                                    // spares
        
        public uint checksum;                                     // message checksum - 32bit addition

    }
    //static_assert(sizeof(RKS2MC_Init) == 132, "Wrong msg size, Unplanned IDD change");


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MC2RKS_Init
    {
        public MC2RKS_Init()
        {
            sw_version = new SwVersion[(int)McBoards.McBoardMcNumOfBoards];
            spare = new byte[6];
            spare5 = new uint[2];
            nvram_fingerprint = new uint[(int)McStickMotorIndex.StickNumOfMotors];
            spares_middle2 = new uint[20];
            spare4 = new uint[8];
            pbit_status = new BitFieldType[(int)McBitModules.NumOfBitModules];
            spare3 = new uint[11];
            error_state = new eModuleState[(int)McBitModules.NumOfBitModules];
            spare2 = new byte[3];
            spares_bit_2 = new uint[8];
            spare1 = new uint[13];

        }
        //static constexpr cOpcode def_opcode = OP_MC_RKS_INIT;
        //static constexpr const char* name = "Mc2Rks Init";
        //static constexpr uint idd_version[3] = {RKS_MC_IDD_VERSION_MAJOR, RKS_MC_IDD_VERSION_MINOR, RKS_MC_IDD_VERSION_PATCH};

        public cHeader header;                                  // message header
        public uint echo_counter;                           // echo counter for last message received

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McBoards.McBoardMcNumOfBoards)]
        public SwVersion[] sw_version;     // MC Slow, MC Fast, Spare

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] spare;                                  //  Spare


        public byte u8McFastIsCrashed;                 //
        public byte u8McSlowIsCrashed ;                       //
        public  ushort u16McFastCrashAddress ;                  //
                    public ushort u16McFastCrashHelpReg ;                  //
        public uint u32McFastCrashCfsr ;                     //
        public ushort u16McSlowCrashAddress ;                  //
        public ushort u16McSlowCrashHelpReg ;                  //
        public uint u32McSlowCrashCfsr ;                     //

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] spare5;                              // spares
        
        public ulong time_tag_from_power_up ;                 // time tag from board power up

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McStickMotorIndex.StickNumOfMotors)]
        public uint[] nvram_fingerprint; // M1, M2, M3
       
        public float scb_board_version;                           //  SCB Board version
        public float scb_board_id;                                //  SCB Board ID
        public float hub_board_version;                           //  Hub (SCRB) Board version
        public float hub_board_id;                                //  Hub (SCRB) Board ID

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public uint[] spares_middle2;                     // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] spare4;                              // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McBitModules.NumOfBitModules)]
        public BitFieldType[] pbit_status;    // power on built in test report

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        public uint[] spare3;                             // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McBitModules.NumOfBitModules)]
        public eModuleState[] error_state;    // error modules state machine


        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] spare2;                               // spares

        public eOperationMode operation_state ;                  // operation mode - OPER or SERVICE

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] spares_bit_2;                        // spares


        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 13)]
        public uint[] spare1;                             // spares
        
        public uint checksum ;                               // message checksum - 32bit addition
    }
    //constexpr uint MC2RKS_INIT_SIZE = sizeof(MC2RKS_Init);
    //static_assert(sizeof(MC2RKS_Init) == 392, "Wrong msg size, Unplanned IDD change");
}
