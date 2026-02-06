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
    public struct sMicBLeds
    {
        public sLedPattern io_led;
        public sLedPattern stripes_led;
        public sLedPattern estop_led;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VC2MicB_Control
    {
        // 3 static members not sent over UDP.
        //static constexpr cOpcode def_opcode = OP_VC_MICB_CTRL;
        //static constexpr const char* name = "Vc2MicB Control";
        //static constexpr uint32_t idd_version[3] = {VC_MICB_IDD_VERSION_MAJOR, VC_MICB_IDD_VERSION_MINOR, VC_MICB_IDD_VERSION_PATCH};

        public VC2MicB_Control()
        {
            fan_cmd = new byte[(int)eMicbFans.eMicbNumOfFans];
            spare = new byte[4];
            spare3 = new byte[8];
            subsystem_cmd = new eSysState[(int)eMicbBitModules.eMicbNumOfBitModules];
            reset_errors = new eModuleErrorState[(int)eMicbBitModules.eMicbNumOfBitModules];
            spare2 = new UInt32[12];
            spare1 = new float[4];

            header.Opcode = (byte)E_OPCODES.OP_MASTER_PERIODIC_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = MICB_Constants.VC_MICB_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = MICB_Constants.VC_MICB_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = MICB_Constants.VC_MICB_IDD_VERSION_PATCH;

            estop_state_cmd = eEstopStateCmd.eEstopStateNoChange;
            elo_cmd = eMicbEloCmd.eMicbEloEnableMovement;
            for (int i = 0; i < (int)eMicbFans.eMicbNumOfFans; i++)
            {
                fan_cmd[i] = 80; //50% fans
            }
            for (int i = 0; i < (int)eMicbBitModules.eMicbNumOfBitModules; i++)
            {
                subsystem_cmd[i] = eSysState.eActive;
                reset_errors[i] = eModuleErrorState.eModuleErrorStateIgnore;
            }
        }
        public cHeader header;                                       // message header

        // This is spare but not an array.
        public byte spare4;                                       // spares
        public eEstopStateCmd estop_state_cmd;                       // estop command
        public byte u8spare5;                                       // spare
        public eMicbEloCmd elo_cmd;                                  // command to allow for XYZ movement, None - working, Open - disabled

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] fan_cmd;                      // Percentage [%] | 0..100

        public UInt16 cam_sync_offset;                              // camera sync offset in multiples of 1041 usec cycles (960Hz)

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare;                                      // spares
        
        eMicbCamTrigger camera_trigger;                       // eMicbCamTrigger
        public byte spare6;                                       // spare
        public sbyte board_sync_offset;                             // sync offset command in micros
        public sMicBLeds leds;                                       // LED colors

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] spare3;                                    // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public eSysState[] subsystem_cmd;        // system state command for MiccB

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public eModuleErrorState[] reset_errors; // reset errors command

        public byte spare_align;                                  // spare

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
        public UInt32[] spare2;                                  // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public float[] spare1;                                      // spares

        public UInt32 checksum;                                    // message checksum - 32bit addition
    }
    //static_assert(sizeof(VC2MicB_Control) == 112, "Wrong msg size, Unplanned IDD change");

    //TODO: THIS COMMENT IS BAD BUT SINCE THERE IS NOT PRAGMA PACK AT THE DEVICE LEVEL
    //IT CAN NOT BE UNCOMMENT
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MicB2VC_Status
    {
        // 3 static members are not sent over UDP.
        //static constexpr cOpcode def_opcode = OP_MICB_VC_STATUS;
        //static constexpr const char* name = "MicB2Vc Status";
        //static constexpr uint32_t idd_version[3] = {VC_MICB_IDD_VERSION_MAJOR, VC_MICB_IDD_VERSION_MINOR, VC_MICB_IDD_VERSION_PATCH};

        public MicB2VC_Status()
        {
            spare8 = new UInt32[2];
            cbit_teensy_results = new BitFieldType[(int)eMicbBitModules.eMicbNumOfBitModules];
            spare7 = new UInt32[5];
            fan_rpm = new UInt16[(int)eMicbFans.eMicbNumOfFans];
            spare6 = new UInt32[2];
            power_diagnostics = new eMicbPowerDiagnostic[(int)eMicbBoards.eMicbNumOfBoards];
            spare5 = new byte[2];
            rws_manipulator_state = new eMicbRwsManipulatorConnectState[(int)e_sides.eNumOfSides];
            spare10 = new byte[2];
            spare11 = new byte[3];
            wheels_lock_indication = new eMicbWheelsState[(int)e_sides.eNumOfSides];
            spare4 = new byte[2];
            subsystem_state = new eSysState[(int)eMicbBitModules.eMicbNumOfBitModules];
            error_state = new eModuleState[(int)eMicbBitModules.eMicbNumOfBitModules];
            //spare3 = new byte[2];
            spare3 = new byte[4];
            eEstopBtnStatus = new eEstopButtonStatus[(int)eEstopButtonFieldMapping.eEstopNumButton];
            spare2 = new UInt32[18];
            spare1 = new float[6];
            header.Counter = 0;
        }
        public cHeader header;                                                        // message header
        public UInt32 echo_counter;                                               // echo counter for last message received
        public byte spare9;                                                      // spare
        public byte elo_state;                                                   // elo state
        public byte robot_connected;                                             // robot connected indication
        public byte surgeon_connected;                                           // surgeon connected indication

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] spare8;                                                  // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbBitModules.eMicbNumOfBitModules)]
        public BitFieldType[] cbit_teensy_results;              // cbit results

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public UInt32[] spare7;                                                  // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public UInt16[] fan_rpm;                                    // fan rpm

        public UInt32 pedal_indication;                                           // pedal indication
        public UInt64 time_tag_from_power_up;                                     // time tag from board power up in micros
        public UInt32 estop_additional_info;                                    // estop info ,according to eEstopStatusAdditionalInfo bitwise mapping
        //public eEstopStatusAdditionalInfo estop_additional_info;                  // estop info ,according to eEstopStatusAdditionalInfo bitwise mapping

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] spare6;                                                  // spares

        public eEstopStateCmd estop_state_cmd_echo;                                 // estop cmd echo
        public eSystemEstopReadyness eEstopReady;                                   // eSystemEstopReadyness
        public byte power_button_state;                                          // power button state
        public byte camera_trigger_status;                                       // camera trigger status
        public UInt64 camera_trigger_time_tag;                                    // camera trigger time tag
        public Int16 cam_sync_offset;                                             // camera sync offset in microseconds

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eMicbBoards.eMicbNumOfBoards)]
        public eMicbPowerDiagnostic[] power_diagnostics;            // eMicbPowerDiagnostic

        public byte eYaxisLockState;                                             // 0 - not locked , 1 - locked , 2 - error ?
        public byte eYaxisPosState;                                              // position state: 0 - storage , 1 - transition , 2 - oper

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare5;                                                   // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public eMicbRwsManipulatorConnectState[] rws_manipulator_state;  // physical connection status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare10;                                                  // spare

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public byte[] spare11;                    

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public eMicbWheelsState[] wheels_lock_indication;                // eMicbWheelsState

        public sbyte board_sync_offset;                                            // sync offset echo
        public eEstopSysStatus estop_sys_status;                                      // HW circuit estop status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare4;                                                   // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public eSysState[] subsystem_state;                     // system state for MiccB

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public eModuleState[] error_state;                      // error modules state machine

        public sMicBLeds led_patterns;                                              // LED patterns

        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        //public byte[] spare3;                                                   // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare3;                                                   // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public eEstopButtonStatus[] eEstopBtnStatus;                 // ESTOP button status                                                   // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 18)]
        public UInt32[] spare2;                                                 // spares

        public eOperationMode operation_status;                                     // echo of operation status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public float[] spare1;                                                     // spares
        
        public UInt32 checksum;                                                   // message checksum - 32bit addition
    }
//static_assert(sizeof(MicB2VC_Status) == 240, "Wrong msg size, Unplanned IDD change");
}
