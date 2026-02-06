using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public static class MOCB_Constants
    {
        public const int VC_MOCB_IDD_VERSION_MAJOR = 3;
        public const int VC_MOCB_IDD_VERSION_MINOR = 2;
        public const int VC_MOCB_IDD_VERSION_PATCH = 1;

        //constexpr byte OP_VC_MOCB_CTRL = OP_MASTER_PERIODIC_COMMAND;
        //constexpr byte OP_MOCB_VC_STATUS = OP_CONTROLLER_PERIODIC_STATUS;

        //constexpr byte OP_VC_MOCB_INIT = OP_MASTER_INIT_COMMAND;
        //constexpr byte OP_MOCB_VC_INIT = OP_CONTROLLER_INIT_STATUS;

        //constexpr byte OP_VC_MOCB_CALIB = OP_MASTER_CALIB_COMMAND;
        //constexpr byte OP_MOCB_VC_CALIB_STATUS = OP_CONTROLLER_CALIB_STATUS;

    }
    public enum e_mocB_led
    {
            eMocBLedsStart = 0,
            eMocBLedCoaxL = eMocBLedsStart,
            eMocBLedCoaxR,
            eMocBLedParax,
            eMocBLedTracking,
            E_MOCB_LED_NUM
    }

    public enum e_mocb_subsystem_modules
    {
        E_MOCB_BIT_MODULE_COAX_L = 0,
        E_MOCB_BIT_MODULE_COAX_R,
        E_MOCB_BIT_MODULE_LIGHT_PARAX,
        E_MOCB_BIT_MODULE_LIGHT_TRACKING,
        E_MOCB_BIT_MODULE_SENSORS,
        E_MOCB_BIT_MODULE_GENERAL,
        E_MOCB_NUM_BIT_MODULES
    }

    public enum e_mocb_bit_units
    {
        E_MOCB_BIT_UNIT_COAX_L = 0,
        E_MOCB_BIT_UNIT_COAX_R,
        E_MOCB_BIT_UNIT_LIGHT_PARAX,
        E_MOCB_BIT_UNIT_LIGHT_TRACKING,
        E_MOCB_BIT_UNIT_SENSORS,
        E_MOCB_BIT_UNIT_GENERAL,
        E_MOCB_BIT_UNIT_ROLL_MOTION,
        E_MOCB_BIT_UNIT_IRIS_MOTION,
        E_MOCB_NUM_BIT_UNITS
    }

    public enum e_mocB_limit_switch_modes
    {
        E_MOCB_LIMIT_SWITCH_IN_BETWEEN = 1 << 0,
        E_MOCB_LIMIT_SWITCH_POSITIVE = 1 << 1,
        E_MOCB_LIMIT_SWITCH_NEGATIVE = 1 << 2
    }

    public enum e_mocB_roll_limit_switch_modes
    {
        E_MOCB_ROLL_LIMIT_SWITCH_IN_BETWEEN = 1 << 0,
        E_MOCB_ROLL_LIMIT_SWITCH_POSITIVE = 1 << 1,
        E_MOCB_ROLL_LIMIT_SWITCH_NEGATIVE = 1 << 2,
        E_MOCB_ROLL_LIMIT_SWITCH_IN_ZERO = 1 << 3
    }

    public enum e_mocB_self_calibration_command : byte
    {
        eSelfCalibOff = 0,
        eSelfCalibOn = 1
    }

    public enum e_mocB_self_calibration_status : byte
    {
        eSelfCalibNotDone = 0,
        eSelfCalibMoveLeft = 1,
        eSelfCalibMoveRight = 2,
        eSelfCalibMoveCenter =  3,
        eSelfCalibDone =  4
    }

    public enum eMocbDecorationLeds : byte
    {
        eCHU = 0,
        eMocbNumOfDecorationLeds
    }


}
