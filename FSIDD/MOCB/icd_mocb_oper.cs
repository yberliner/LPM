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
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sMocBLeds
    {
        public sLedPattern chu_led; // chu led decoration
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VC2MocB_Control
    {
        public VC2MocB_Control()
        {
            led_intensity = new byte[4];
            
            spare3 = new byte[4];
            spare6 = new UInt32[2];
            subsystem_cmd = new eSysState[(int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES];
            reset_errors = new eModuleErrorState[(int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES]; 
            spare4 = new byte[2];
            spare2 = new UInt32[4];
            spare1 = new float[4];

            header.Opcode = (byte)E_OPCODES.OP_MASTER_PERIODIC_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = MOCB_Constants.VC_MOCB_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = MOCB_Constants.VC_MOCB_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = MOCB_Constants.VC_MOCB_IDD_VERSION_PATCH;

            for (int i = 0; i < (int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES; i++)
            {
                subsystem_cmd[i] = eSysState.eActive;
                reset_errors[i] = eModuleErrorState.eModuleErrorStateIgnore;
            }
            Array.Fill(led_intensity, (byte)20, 0, (int)e_mocB_led.E_MOCB_LED_NUM);
            Array.Fill(subsystem_cmd, eSysState.eActive, 0,
                      (int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES);
        }
        //static constexpr cOpcode def_opcode = OP_VC_MOCB_CTRL;
        //static constexpr const char* name = "Vc2MocB Control";
        //static constexpr UInt32 idd_version[3] = {VC_MOCB_IDD_VERSION_MAJOR, VC_MOCB_IDD_VERSION_MINOR, VC_MOCB_IDD_VERSION_PATCH};

        public cHeader header;                                         // message header
        public sLedPattern chu_led;                                    // decoration led pattern

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] led_intensity;                  // LED intensity

        public UInt16 led_sync_bitmap;                               // bitmap for activating cycles (LSB is right after the sync start) - amir , can move to reconfigure stage since it is constant
        public float motor_iris_l;                                     // command for motor iris_l
        public float motor_iris_r;                                     // command for motor iris_r
        public float motor_roll;                                       // command for motor roll
        public sbyte board_sync_offset ;                               // sync command in microseconds

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public eModuleErrorState[] reset_errors; // reset errors command

        public e_mocB_self_calibration_command perform_self_calib;     // perform self calibration using limits

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare3;                                      // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] spare6;                                     // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public eSysState[] subsystem_cmd;        // system state command for MocB

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare4;                                      // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public UInt32[] spare2;                                     // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] spare1;                                        // spares

        public UInt32 checksum;                                      // message checksum - 32bit addition
    }
    //static_assert(sizeof(VC2MocB_Control) == 92, "Wrong msg size, Unplanned IDD change");


    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MocB2VC_Status
    {
        public MocB2VC_Status()
        {
            spares = new UInt32[4];
            subsystem_state = new eSysState[6];
            spare_align = new byte[2];
            error_state = new eModuleState[6];
            spare_align_2 = new byte[2];
            cbit_results = new BitFieldType[8];
            spare1 = new float[5];
            u8SpareAlign = new byte[56];
            header.Counter = 0;
        }

        //static constexpr cOpcode def_opcode = OP_MOCB_VC_STATUS;
        //static constexpr const char* name = "MocB2Vc Status";
        //static constexpr UInt32 idd_version[3] = { VC_MOCB_IDD_VERSION_MAJOR, VC_MOCB_IDD_VERSION_MINOR, VC_MOCB_IDD_VERSION_PATCH};
        public cHeader header;                                      // message header
        public UInt32 echo_counter;                             // echo counter for last message received
        public byte spare6;                                    // spare
        public byte limit_switch_iris_l;                       // e_mocB_limit_switch_modes
        public byte limit_switch_iris_r;                       // e_mocB_limit_switch_modes
        public byte limit_switch_roll;                         // e_mocB_roll_limit_switch_modes
        public UInt32 spare2;                                   // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public UInt32[] spares;                                // spares
        
        public byte led1_intensity;                            // LED intensity
        public byte led2_intensity;                            // LED intensity
        public byte led3_intensity;                            // LED intensity
        public byte led4_intensity;                            // LED intensity
        public UInt16 led_sync_bitmap;                          // bitmap for activating cycles (LSB is right after the sync start)
        public byte watchdog_status;                           // status that indicated expiration from MOCB WD (estop_reset_N_status)
        public eEstopSysStatus estop_sys_status;                  // HW circuit estop status
        public float motor_iris_l;                                // encoder for motor iris_l
        public float motor_iris_r;                                // encoder for motor iris_r
        public float motor_roll;                                  // encoder for motor roll
        public UInt64 time_tag_from_power_up;                   // time tag from board power up
        public byte estop_WD_cmd;                              // indicate if WD command to Toggle
        public sbyte board_sync_offset;                          // sync offset command echo
        public e_mocB_self_calibration_status self_calib_status;  // performing self calibration using limits
        public byte spare3;                                    // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public eSysState[] subsystem_state; // system state for MocB

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare_align;                            // spares
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public eModuleState[] error_state;  // error modules state machine

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare_align_2;                          // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public BitFieldType[] cbit_results;   // spares

        public eOperationMode operation_status;                   // echo of operation status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public float[] spare1;                                   // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 56)]
        public byte[] u8SpareAlign;                          // spares

        public UInt32 checksum;                                 // message checksum - 32bit addition
    }
    //static_assert(sizeof(MocB2VC_Status) == 200, "Wrong msg size, Unplanned IDD change");
}
