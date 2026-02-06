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
using MSGS;

namespace MSGS
{
    // static constexpr UInt32 SERIAL_NUMBER_LEN = 16;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VC2MocB_Init
    {
        //static constexpr cOpcode def_opcode = OP_VC_MOCB_INIT;
        //static constexpr const char* name = "Vc2MocB Init";
        //static constexpr UInt32 idd_version[3] = {VC_MOCB_IDD_VERSION_MAJOR, VC_MOCB_IDD_VERSION_MINOR, VC_MOCB_IDD_VERSION_PATCH};

        public cHeader header;                                        // message header

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public sLedInterval[] led_intervals; // sLedInterval

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
        public sRgbColor[] led_colors;          // sRgbColor

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public byte[] spare3;                                    // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public UInt32[] spare2;                                    // spares

        eOperationMode operation_state;                        // operation mode - OPER or SERVICE
        public float roll_offset;                                     // roll offset in case EMB crashed
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public float[] spare1;                                       // spares
        
        public UInt32 checksum;                                     // message checksum - 32bit addition


        public VC2MocB_Init()
        {
            led_intervals = new sLedInterval[(int)eLedIntervalPattern.eNumOfLedIntervalPatterns];
            led_colors = new sRgbColor[(int)eLedColorPattern.eNumOfLedColorPatterns];
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string iniPath = Path.Combine(exeDir, "VisionComputer.global.ini");

            Utils.LedIniLoader.Load(iniPath, out led_colors, out led_intervals);

            spare3 = new byte[12];
            spare2 = new UInt32[3];
            spare1 = new float[3];

            header.Opcode = (byte)E_OPCODES.OP_MASTER_INIT_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = MOCB_Constants.VC_MOCB_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = MOCB_Constants.VC_MOCB_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = MOCB_Constants.VC_MOCB_IDD_VERSION_PATCH;
            operation_state = eOperationMode.eOperationModeNormal;

            //InitLeds();
        }

        //private void InitLeds()
        //{
        //    sLedInterval ledint = led_intervals[0];
        //    FillLedInt(0, 255, 0, 0, 0, 4);
        //    FillLedInt(8, 120, 1, 120, 7, 4);
        //    FillLedInt(64, 64, 2, 62, 64, 10);
        //    FillLedInt(4, 60, 3, 60, 4, 4);

        //    FillLedColor(0, 0, 0, 0);
        //    FillLedColor(0, 214, 1, 255);
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
    //static_assert(sizeof(VC2MocB_Init) == 136, "Wrong msg size, Unplanned IDD change");

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MocB2VC_Init
    {
        public MocB2VC_Init()
        {
            spare = new UInt32[20];
            spare4 = new byte[4];
            spare3 = new byte[4];
            error_state = new eModuleState[6];
            spare_align = new byte[2];
            spare2 = new UInt32[2];
            pbit_status = new BitFieldType[8];
            serial_number = new byte[16];
            PWM_DC = new float[4];
            currrent_data_mA = new float[4];
            u8SpareAlign = new byte[128];
        }
        //static constexpr cOpcode def_opcode = OP_MOCB_VC_INIT;
        //static constexpr const char* name = "MocB2Vc Init";
        //static constexpr UInt32 idd_version[3] = {VC_MOCB_IDD_VERSION_MAJOR, VC_MOCB_IDD_VERSION_MINOR, VC_MOCB_IDD_VERSION_PATCH};

        public cHeader header;                                         // message header
        public UInt32 echo_counter;                                  // echo counter for last message received
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public UInt32[] spare;                                     // spare

        public SwVersion mocb_version;                                 // mocb version

        public UInt64 time_tag_from_power_up;                        // time tag from board power up

        public UInt32 spare5;                                        // pbit status
        public e_mocB_self_calibration_status self_calibration_status; // self calibration status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare4;                                      // spares

        public BoardVersion board_version;                             // board version

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare3;                                      // spares

        public byte u8IsCrashed;                                    // is EMB crashed

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public eModuleState[] error_state;       // error modules state machine

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare_align;                                 // spares

        public float roll_calib_left;                                  // roll calibration left

        public float roll_calib_right;                                 // roll calibration right
        public UInt16 u16CrashedAddres;                              // EMB crashed address
        public UInt16 u16CrashedfBar;                                //
        public UInt32 u32Crashfsr;                                   // EMB crashed fsr

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] spare2;                                     // spare

        public float r32boardversion;                                  // board version
        public float r32boardId;                                       // board ID
        public eOperationMode operation_state;                         // operation mode - OPER or SERVICE
        public float motor_iris_l;                                     // encoder for motor iris_l
        public float motor_iris_r;                                     // encoder for motor iris_r
        public float motor_roll;                                       // encoder for motor roll


        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public BitFieldType[] pbit_status;         // power on built in test report

        public float spare1;                                           // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] serial_number;                 // board serial number

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] PWM_DC;                             // PWM DC for LEDs

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] currrent_data_mA;                   // current data in mA for LEDs

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] u8SpareAlign;                              // spares

        public UInt32 checksum;                                      // message checksum - 32bit addition   
    }
    // static_assert(sizeof(MocB2VC_Init) == 400, "Wrong msg size, Unplanned IDD change");
}
