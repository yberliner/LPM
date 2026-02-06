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
    public struct sRcLeds
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eRcRobotLeds.eRcNumOfRobotLedStrips)]
        public sLedPattern[] led_robot_decoration; // Robot Decoration LEDs

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public sLedPattern[] led_plunger_strip;               // Plunger LEDs

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)] 
        public sLedPattern[] led_plunger_button;              // Plunger Button LEDs

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)] 
        public sLedPattern[] led_tool_exchange;               // Tool Exchange LEDs

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)] 
        public sLedPattern[] led_logo;                        // Logo LEDs

        public sLedPattern led_table;                                    // Table LED
        public sLedPattern led_estop;                                    // ESTOP LED

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] 
        public sLedPattern[] spare;                                     // spares

        // Constructor to initialize arrays
        public sRcLeds()
        {
            //Console.WriteLine("Robot Leds Constructor");
            led_robot_decoration = new sLedPattern[(int)eRcRobotLeds.eRcNumOfRobotLedStrips];
            led_plunger_strip = new sLedPattern[(int)e_sides.eNumOfSides];
            led_plunger_button = new sLedPattern[(int)e_sides.eNumOfSides];
            led_tool_exchange = new sLedPattern[(int)e_sides.eNumOfSides];
            led_logo = new sLedPattern[(int)e_sides.eNumOfSides];
            spare = new sLedPattern[3];

            //led_table = new sLedPattern();
            //led_estop = new sLedPattern();
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RKS2RC_Control
    {
        public RKS2RC_Control()
        {
            header.Opcode = (byte)E_OPCODES.OP_MASTER_PERIODIC_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = RC_Constants.VC_RC_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = RC_Constants.VC_RC_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = RC_Constants.VC_RC_IDD_VERSION_PATCH;

           
            subsystem_cmd = new eSysState[(int)eRcSubsystems.eRcNumOfSubsystems];
            reset_errors = new eModuleErrorState[(int)eRcSubsystems.eRcNumOfSubsystems];

            for (int i = 0; i < (int)(int)eRcSubsystems.eRcNumOfSubsystems; i++)
            {
                subsystem_cmd[i] = i < 4 ? eSysState.eActive : eSysState.eInactive; //eSysState.eInactive;
                reset_errors[i] = eModuleErrorState.eModuleErrorStateIgnore; //for inetgration
            }

            spare_leds = new byte[16];
            fla_cmd = new sRcFlaCmd[(int)e_sides.eNumOfSides];

            //manipulator_cmd = new sRcManipulatorCmd[(int)e_sides.eNumOfSides];
            Fill_manipulator_cmd(ref manipulator_cmd_left);
            Fill_manipulator_cmd(ref manipulator_cmd_right);

            spare_align = new byte[2];
            spare1 = new UInt32[2];
            spare2 = new UInt32[2];

            estop_cmd = eEstopStateCmd.eEstopStateNoChange;
            led_patterns = new sRcLeds();
            ibit_test = new UInt32[(int)eRcIbits.eRcIbitNumOfItems];
        }

        private void Fill_manipulator_cmd(ref sRcManipulatorCmd manipulator_cmd)
        {
            //TEMP integration
            //for (int side = 1; side < (int)e_sides.eNumOfSides ; side++)
            {
                int move_dist = -10;
                manipulator_cmd.algo_type = new byte[(int)eMotorIndex.eNumOfMotors];
                manipulator_cmd.target = new cRcPose();
                for (int motor_num = 0; motor_num < (int)(int)eMotorIndex.eNumOfMotors; motor_num++)
                {
                    manipulator_cmd.algo_type[motor_num] = (byte)eRcAlgoType.eRcAlgoTypePositionLoop;
                    manipulator_cmd.target.m1 = move_dist;
                    manipulator_cmd.target.m2 = move_dist;
                    manipulator_cmd.target.m3 = move_dist;
                    manipulator_cmd.target.m4 = move_dist;
                    manipulator_cmd.target.m5 = -35;// move_dist;
                    manipulator_cmd.target.m6 = move_dist;
                    manipulator_cmd.target.plunger = 30;// move_dist;
                    manipulator_cmd.slow_mode = eRcOperationMode.eRcOperationModeFast;

                }
                
            }
            
        }

        //static constexpr cOpcode def_opcode = OP_RKS_RC_CTRL;
        //static constexpr const char* name = "Rks2Rc Control";
        //static constexpr uint32_t idd_version[3] = {RKS_RC_IDD_MAJOR, RKS_RC_IDD_MINOR, RKS_RC_IDD_PATCH};

        public cHeader header;                                     // message header

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] spare_leds;                             // spare

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eRcSubsystems.eRcNumOfSubsystems)]
        public eModuleErrorState[] reset_errors; // reset errors command
        
        public eEstopStateCmd estop_cmd;                           // eEstopState - reflect internal ESTOP Status
        public byte board_sync_offset;                           // sync offset command, -10 to 10


        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public sRcFlaCmd[] fla_cmd;                     // fla command

        //[MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        //public sRcManipulatorCmd[] manipulator_cmd;     // manipulator command
        public sRcManipulatorCmd manipulator_cmd_left;     
        public sRcManipulatorCmd manipulator_cmd_right;
        
        
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eRcSubsystems.eRcNumOfSubsystems)]
        public eSysState[] subsystem_cmd;        // system state command for RC

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare_align;                             // spares

        public sRcLeds led_patterns;                               // LED patterns
        public UInt32 fpga_diagnostic_header;                    // FPGA diagnostic header data
        public UInt32 lock_status;                               // lock sensor status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] spare2;                                 // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eRcIbits.eRcIbitNumOfItems)]
        public UInt32[] ibit_test;                                 // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public UInt32[] spare1;                                 // spares

        public UInt32 checksum;                                  // message checksum - 32bit addition
   }
    //static_assert(sizeof(RKS2RC_Control) == 308, "Wrong msg size, Unplanned IDD change");

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RC2RKS_Status
    {
        public RC2RKS_Status()
        {
            spare5 = new UInt32[4];
            spare4 = new UInt32[4];
            fla_status = new sRcFlaStatus[(int)e_sides.eNumOfSides];
            manipulator_status = new sRcManipulatorStatus[(int)e_sides.eNumOfSides];
            spare3 = new UInt32[4];
            error_state = new eModuleState[(int)eRcSubsystems.eRcNumOfSubsystems];
            
            wheel_sensors = new UInt16[(int)e_sides.eNumOfSides];
            subsystem_state = new eSysState[(int)eRcSubsystems.eRcNumOfSubsystems];
            spare_align = new byte[2];
            spare1 = new UInt32[7];
            header.Counter = 0;

        }
        //static constexpr cOpcode def_opcode = OP_RC_RKS_STATUS;
        //static constexpr const char* name = "Rc2Rks Status";
        //static constexpr uint32_t idd_version[3] = {RKS_RC_IDD_MAJOR, RKS_RC_IDD_MINOR, RKS_RC_IDD_PATCH};

        public cHeader header;                                         // message header
        public UInt32 echo_counter;                                // echo counter for last message received
        public byte rc_estop_additional_info;                     // estop info, according to eRcEstopStatusAdditionalInfo bitwise mapping
        public byte safety_button_status;                         // eRcButtonState
        public byte tool_exchange_buttons;                        // 1 - Left, 2 - Right, 3 - both
        public eEstopStateCmd estop_cmd_echo;                        // eEstopState

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public UInt32[] spare5;                                   // spares

        public UInt64 safety_button_time_tag;                      // last button press time
        public UInt64 time_tag_from_power_up;                      // time from power up in RC Clock


        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public UInt32[] spare4;                                   // spares

        public sRcBitData cbit_status;                               // RC CBIT status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public sRcFlaStatus[] fla_status;                 // Floor Lock Actuator status


        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public sRcManipulatorStatus[] manipulator_status; // manipulator status

        public UInt64 camera_trigger_time_tag;                     // Camera trigger in RC Clock (MicB -> RC -> VC), 0 if counter overflow

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public UInt32[] spare3;                                   // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eRcSubsystems.eRcNumOfSubsystems)]
        public eModuleState[] error_state;         // error modules state machine

        public sbyte board_sync_offset;                             // sync offset echo

        public byte spare2;                                       // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public UInt16[] wheel_sensors;                  // wheel sensor status (eRcWheelsMask)

        public eOperationMode operation_status;                      // echo of operation status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)eRcSubsystems.eRcNumOfSubsystems)]
        public eSysState[] subsystem_state;        // system state for RC

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare_align;                               // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 7)]
        public UInt32[] spare1;                                   // spares

        public UInt32 checksum;                                    // message checksum - 32bit addition
}
//static_assert(sizeof(RC2RKS_Status) == 472, "Wrong msg size, Unplanned IDD change");
}
