using MSGS;
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
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sMcLeds
    {
        public sMcLeds()
        {
            joystick_led_patterns = new sLedPattern[(int)McJoystickLeds.JoystickNumOfLeds];
        }

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McJoystickLeds.JoystickNumOfLeds)]
        public sLedPattern[] joystick_led_patterns;      // joysticks LED pattern
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RKS2MC_Control
    {
        public RKS2MC_Control()
        {
            header.Opcode = (byte)E_OPCODES.OP_MASTER_PERIODIC_COMMAND;
            header.Counter = 0;
            header.VersionIdd.VersionMajor = MC_Constants.RKS_MC_IDD_VERSION_MAJOR;
            header.VersionIdd.VersionMinor = MC_Constants.RKS_MC_IDD_VERSION_MINOR;
            header.VersionIdd.VersionPatch = MC_Constants.RKS_MC_IDD_VERSION_PATCH;

            spare4 = new byte[6];
            haptic_enable = new McHapticState[(int)e_sides.eNumOfSides];
            vibration_mode = new VibrationMode[(int)e_sides.eNumOfSides];

            for (int i = 0; i < (int)e_sides.eNumOfSides; i++)
            {
                haptic_enable[i] = McHapticState.HapticStateEnable;
                vibration_mode[i] = VibrationMode.VibrateMode1;
            }
            estop_cmd = eEstopStateCmd.eEstopStateNoChange;
            elo_cmd = McEloState.EloEnableMovement;

            monitor_config_cmd = new uint[(int)McMonitorCmd.McMonitorCmdNum];


            for (int i = 0; i < (int)McMonitorCmd.McMonitorCmdNum; i++)
            {
                //monitor_config_cmd[i] = 
            }


            haptic_forces = new float[(int)e_sides.eNumOfSides * (int)McAxis.AxisNumOfPosItems];
            reset_errors = new eModuleErrorState[(int)McBitModules.NumOfBitModules];
            subsystem_cmd = new eSysState[(int)McBitModules.NumOfBitModules];

            for (int i = 0; i < (int)McBitModules.NumOfBitModules; i++)
            {
                reset_errors[i] = eModuleErrorState.eModuleErrorStateIgnore;
                subsystem_cmd[i] = eSysState.eActive;
            }

            spare3 = new uint[4];
            fan_control_cmd = new byte[(int)McFans.NumOfFans];
            for (int i = 0; i < (int)McFans.NumOfFans; i++)
            {
                fan_control_cmd[i] = 80;
            }
            spare5 = new byte[2];
            spare1 = new uint[2];
            led_patterns = new sMcLeds();

        }
        //static constexpr cOpcode def_opcode = OP_RKS_MC_CTRL;
        //static constexpr const char* name = "Rks2Mc Control";
        //static constexpr uint idd_version[3] = {RKS_MC_IDD_VERSION_MAJOR, RKS_MC_IDD_VERSION_MINOR, RKS_MC_IDD_VERSION_PATCH};

        public cHeader header;                                         // message header

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public byte[] spare4;                                      // spare

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)e_sides.eNumOfSides))]
        public McHapticState[] haptic_enable;              // haptic enable

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)e_sides.eNumOfSides))]
        public VibrationMode[] vibration_mode;             // eVibrationMode

        public eEstopStateCmd estop_cmd;                               // eEstopStateCmd - cmd for open / no change
        public McEloState elo_cmd;                                    // eMcEloState

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)McMonitorCmd.McMonitorCmdNum))]
        public uint[] monitor_config_cmd;          // 0 - key, 1 - val

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)e_sides.eNumOfSides) * (int)McAxis.AxisNumOfPosItems)]
        public float[] haptic_forces; // haptic feedback forces

        public byte board_sync_offset;                               // sync offset command, -50 to 50

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)McBitModules.NumOfBitModules))]
        public eModuleErrorState[] reset_errors;     // reset errors command

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)McBitModules.NumOfBitModules))]
        public eSysState[] subsystem_cmd;            // system state command for MC

        public byte spare2;                                         // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] spare3;                                     // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McFans.NumOfFans)]
        public byte[] fan_control_cmd;              // PWM Fan control command in %

        public byte spare6;

        public sMcLeds led_patterns;                                   // joysticks LEDs

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare5;                                      // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] spare1;                                     // spares

        public uint checksum;                                      // message checksum - 32bit addition
    }
    //static_assert(sizeof(RKS2MC_Control) == 116, "Wrong msg size, Unplanned IDD change");

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MC2RKS_Status
    {
        public MC2RKS_Status()
        {
            cbit_results = new BitFieldType[(int)McBitUnits.NumOfBitUnits];
            spare4 = new byte[8];
            monitor_config_cmd_echo = new uint[(int)McMonitorCmd.McMonitorCmdNum];
            haptic_forces_echo = new float[(int)e_sides.eNumOfSides * (int)McStickMotorIndex.StickNumOfMotors];
            haptic_enable_echo = new McHapticState[(int)e_sides.eNumOfSides];
            vibration_mode_echo = new VibrationMode[(int)e_sides.eNumOfSides];
            mc_predicted_pose = new cMcPose[(int)e_sides.eNumOfSides];
            forceps_actuation_intensity = new uint[(int)e_sides.eNumOfSides];
            joystick_homing = new McHomingState[(int)e_sides.eNumOfSides];
            spare3 = new byte[4];
            error_state = new eModuleState[(int)McBitModules.NumOfBitModules];
            subsystem_state = new eSysState[(int)McBitModules.NumOfBitModules];
            spare2 = new byte[2];
            fan_speed = new ushort[(int)McFans.NumOfFans];
            spare1 = new uint[15];
            header.Counter = 0;
            mc_pos = new cMcPose[(int)e_sides.eNumOfSides];
            for (int i = 0; i < (int)e_sides.eNumOfSides; i++)
            {
                mc_pos[i] = new cMcPose();
                mc_predicted_pose[i] = new cMcPose();
                haptic_enable_echo[i] = new McHapticState();
                vibration_mode_echo[i] = new VibrationMode();
                joystick_homing[i] = new McHomingState();
                forceps_actuation_intensity[i] = 0;
            }

        }
        //static constexpr cOpcode def_opcode = OP_MC_RKS_STATUS;
        //static constexpr const char* name = "Mc2Rks Status";
        //static constexpr uint idd_version[3] = {RKS_MC_IDD_VERSION_MAJOR, RKS_MC_IDD_VERSION_MINOR, RKS_MC_IDD_VERSION_PATCH};

        public cHeader header;                                             // message header
        public uint echo_counter ;                                      // echo counter for last message received                                        // 12 - 15
        public byte spare5 ;                                             // spare
        public McEloState elo_cmd_echo ;                                   // eMcEloState
        public eEstopStateCmd estop_cmd_echo ;                              // eEstopStateCmd echo
        public eEstopSysStatus estop_sws_status_in ;                        // estop status from HW (input to Teensy)
        public ulong time_tag_from_power_up ;                            // time tag from board power up

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McBitUnits.NumOfBitUnits)]
        public BitFieldType[] cbit_results;                // MC CBIT status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] spare4;                                          // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McMonitorCmd.McMonitorCmdNum)]
        public uint[] monitor_config_cmd_echo;         //  0 - cmd, 1 - val

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides * (int) McStickMotorIndex.StickNumOfMotors)]
        public float[] haptic_forces_echo; // echo for haptic feedback forces

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public McHapticState[] haptic_enable_echo;             // haptic enable echo

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public VibrationMode[] vibration_mode_echo;            // eVibrationMode echo

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public cMcPose[] mc_pos;                                // mc position

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public cMcPose[] mc_predicted_pose;                     // mc velocity

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public uint[] forceps_actuation_intensity;          // forceps intensity (0-1024)

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)e_sides.eNumOfSides)]
        public McHomingState[] joystick_homing;                // eMcHomingState

        public byte mc_estop_additional_data ;                           // according to eEstopMcAdditionalData

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public byte[] spare3;                                          // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)McBitModules.NumOfBitModules))]
        public eModuleState[] error_state;               // error modules state machine

        public sbyte board_sync_offset ;                                   // sync offset echo

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ((int)McBitModules.NumOfBitModules))]
        public eSysState[] subsystem_state;              // system state for MC

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] spare2;                                          // spares

        public eOperationMode operation_status ;                            // echo of operation status

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = (int)McFans.NumOfFans)]
        public ushort[] fan_speed;                       // Fan speed in RPM

        public ushort spare6 ;                                            // spares

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 15)]
        public uint[] spare1;                                        // spares

        public uint checksum ;                                          // message checksum - 32bit addition
    }
    //constexpr uint MC2RKS_STATUS_SIZE = sizeof(MC2RKS_Status);
    //static_assert(sizeof(MC2RKS_Status) == 360, "Wrong msg size, Unplanned IDD change");
}
