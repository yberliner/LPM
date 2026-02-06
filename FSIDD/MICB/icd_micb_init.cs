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
using System.Runtime.InteropServices;

namespace MSGS
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VC2MicB_Init
    {
        // 3 static variables. Not send over UDP.
        //static constexpr cOpcode def_opcode = OP_VC_MICB_INIT;
        //static constexpr const char* name = "Vc2MicB Init";
        //static constexpr uint32_t idd_version[3] = {VC_MICB_IDD_VERSION_MAJOR, VC_MICB_IDD_VERSION_MINOR, VC_MICB_IDD_VERSION_PATCH};

        public cHeader header;                                        // message header

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public sLedInterval[] led_intervals;                       // sLedInterval

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public sRgbColor[] led_colors;          // sRgbColor

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public byte[] spare3;                                    // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public UInt32[] spare2;                                    // spares

        public eOperationMode operation_state;                        // operation mode - OPER or SERVICE

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] spare1;                                       // spares

        public UInt32 checksum;                                     // message checksum - 32bit addition
    
        public VC2MicB_Init()
        {
            led_intervals = new sLedInterval[(int)eLedIntervalPattern.eNumOfLedIntervalPatterns];
            led_colors = new sRgbColor[(int)eLedColorPattern.eNumOfLedColorPatterns];
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string iniPath = Path.Combine(exeDir, "VisionComputer.global.ini");

            Utils.LedIniLoader.Load(iniPath, out led_colors, out led_intervals);

            spare3 = new byte[12];
            spare2 = new UInt32[3];
            spare1 = new float[4];

            header.Opcode = (byte)E_OPCODES.OP_MASTER_INIT_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = MICB_Constants.VC_MICB_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = MICB_Constants.VC_MICB_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = MICB_Constants.VC_MICB_IDD_VERSION_PATCH;
            operation_state = eOperationMode.eOperationModeNormal;

            //InitLeds();
        }

        //private void InitLeds()
        //{
        //    sLedInterval ledint = led_intervals[0];
        //    FillLedInt(0, 255, 0, 0, 0, 4);
        //    FillLedInt(8, 120, 1, 120, 7, 4);
        //    FillLedInt(64,64,2,62,64,10);
        //    FillLedInt(4, 60, 3, 60, 4, 4);

        //    FillLedColor(0, 0, 0, 0);
        //    FillLedColor(0, 214,1,255);
        //    FillLedColor(0, 255, 2, 0);
        //    FillLedColor(0, 0, 3, 255);
        //}

        //private void FillLedColor(byte b, byte g, int id, byte r)
        //{
        //    led_colors[id].Red = r; led_colors[id].Blue = b; led_colors[id].Green = g;
        //}

        //private void FillLedInt(byte falling, byte high, int id, byte low, byte rising, byte scale_ms)
        //{
        //    led_intervals[id].Falling = falling; led_intervals[id].High = high; 
        //    led_intervals[id].Low = low; led_intervals[id].Rising = rising; 
        //    led_intervals[id].ScaleMs = scale_ms;
        //}
    }
    //static_assert(sizeof(VC2MicB_Init) == 136, "Wrong msg size, Unplanned IDD change");
    
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MicB2VC_Init
    {
        // 3 static members are not sent over UDP
        //static constexpr cOpcode def_opcode = OP_MICB_VC_INIT;
        //static constexpr const char* name = "MicB2Vc Init";
        //static constexpr uint32_t idd_version[3] = {VC_MICB_IDD_VERSION_MAJOR, VC_MICB_IDD_VERSION_MINOR, VC_MICB_IDD_VERSION_PATCH};

        public MicB2VC_Init()
        {
            teensy_version = new SwVersion[(int)eMicbBoards.eMicbNumOfBoards];
            spare7 = new byte[8];
            spare = new UInt32[20];
            pbit = new BitFieldType[(int)eMicbBitModules.eMicbNumOfBitModules];
            u16Address = new UInt16[(int)eMicbBoards.eMicbNumOfBoards];
            u16Bfar = new UInt16[(int)eMicbBoards.eMicbNumOfBoards];
            u32Cfsr = new UInt32[(int)eMicbBoards.eMicbNumOfBoards];
            u8IsCrashed = new byte[(int)eMicbBoards.eMicbNumOfBoards];
            spare6 = new byte[33];
            error_state = new eModuleState[(int)eMicbBitModules.eMicbNumOfBitModules];
            spare5 = new byte[5];
            spare4 = new byte[12];
            board_serial_number = new UInt32[4];
            spare3 = new UInt32[5];
            calibration_checksum = new UInt32[8];
            spare2 = new UInt32[10];
            spare1 = new float[2];

        }
        public cHeader header;                                                 // message header
        public UInt32 echo_counter;                                          // echo counter for last message received

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public SwVersion[] teensy_version;                                     // SW version

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] spare7;                                              // spare

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public UInt32[] spare;                                             // spare

        public UInt64 time_tag_from_power_up;                                // time tag from board power up

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public BitFieldType[] pbit;                        // pbit status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public UInt16[] u16Address;                          // address of last crash

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public UInt16[] u16Bfar;                             //

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public UInt32[] u32Cfsr;                             //

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] u8IsCrashed;                          // 1 if the board is crashed, 0 otherwise

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 33)]
        public byte[] spare6;                                             // spare

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public eModuleState[] error_state;                 // error modules state machine

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public byte[] spare5;                                              // spare

        public BoardVersion board_version;                                     // board version

        public eEstopBtnStatusB4PowerDown user_engaged_estop_btn_b4_power_down; // estop button status before power down
        public byte robot_connected;                                        // robot connected indication
        public byte surgeon_connected;                                      // surgeon connected indication

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public byte[] spare4;                                             // spares

        public eOperationMode operation_state;                                 // operation mode - OPER or SERVICE

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public UInt32[] board_serial_number;                                // Board Serial Number

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public UInt32[] spare3;                                             // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public UInt32[] calibration_checksum;                               // calib CS

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public UInt32[] spare2;                                            // spares

        public float r32voltage_board_version;
        public float r32voltage_board_id;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public float[] spare1;   // spares
        
        public UInt32 checksum ; // message checksum - 32bit addition
    }
    //static_assert(sizeof(MicB2VC_Init) == 368, "Wrong msg size, Unplanned IDD change");
}
