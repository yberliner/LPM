using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public static class MICB_Constants
    {
        public const int VC_MICB_IDD_VERSION_MAJOR = 3;
        public const int VC_MICB_IDD_VERSION_MINOR = 3;
        public const int VC_MICB_IDD_VERSION_PATCH = 0;

        //constexpr uint8_t OP_VC_MICB_CTRL = E_OPCODES::OP_MASTER_PERIODIC_COMMAND;
        //constexpr uint8_t OP_MICB_VC_STATUS = E_OPCODES::OP_CONTROLLER_PERIODIC_STATUS;

        //constexpr uint8_t OP_VC_MICB_INIT = E_OPCODES::OP_MASTER_INIT_COMMAND;
        //constexpr uint8_t OP_MICB_VC_INIT = E_OPCODES::OP_CONTROLLER_INIT_STATUS;
    }


    public enum eStorageStateYaxis
    {
        eStorageStateYaxisTransition= 0,
        eStorageStateYaxisInStorage= 1,
        eStorageStateYaxisInWork= 2,
        eStorageStateYaxisError= 3
    }

    public enum eMicbErrors
    {
        eMicbErrorsNoPedalComm = 0,
        eMicbErrorsMismatchYStorage = 1,
        eMicbErrorsMismatchYWork = 2,
        eMicbErrorsYWorkAndStorage = 3
    }

    public enum eMicbBoards
    {
        eMicbFast = 0,
        eMicbSlow = 1,
        eMicbPower = 2,
        eMicbNumOfBoards = 3
    }

    public enum eMicbFans
    {
        eMicbFan1 = 0,
        eMicbFan2 = 1,
        eMicbFan3 = 2,
        eMicbFan4 = 3,
        eMicbNumOfFans = 4
    }

    /// @brief This enum represents the sub-system estop readytness status
    public enum eSystemEstopReadyness : byte
    {
        eReady = 0x55,
        eNotReady = 0xEE
    }

    /// @brief This enum represents the system estop button, if it was pressed before power up
    public enum eEstopBtnStatusB4PowerDown : byte
    {
        eEstopBtnNotPressed = 0x0,
        eEstopBtnPressed = 0x01
    }

    public enum eMicbEloCmd : byte
    {
        eMicbEloDisableMovement = 0x0,
        eMicbEloEnableMovement = 0xAB
    }

    public enum eMicbRwsManipulatorConnectState : byte
    {
        eMicbRwsManipulatorConnected = 1,
        eMicbRwsManipulatorNotConnected = 2,
        eMicbRwsManipulatorUndefined = 3
    }

    public enum eMicbChuState : byte
    {
        eMicbChuStateOn = 1,
        eMicbChuStateOff = 2,
        eMicbChuStateUndefined = 3
    }

    public enum eMicbWheelsState : byte
    {
        eMicbWheelsLocked = 1,
        eMicbWheelsUnlocked = 2,
        eMicbWheelsStateUndefined = 3
    }

    public enum eMicbChuSensor : byte
    {
        eMicbChuSensorStorage = 0,
        eMicbChuSensorOper = 1,
        eMicbChuSensorLock = 2,
        eMicbNumOfChuSensors = 3
    }

    public enum eMicbPowerDiagnostic : byte
    {
        eMicbPowerDiagnosticFault = 1,
        eMicbPowerDiagnosticOk = 2
    }

    public enum eMicbCamTrigger : byte
    {
        eMicbCamTriggerNone = 0,
        eMicbCamTriggerActive = 1
    }

    public enum eMicbBitModules : byte
    {
        eMicbBitModuleGeneral = 0,
        eMicbBitModuleSensors = 1,
        eMicbNumOfBitModules = 2
    }

    public enum eMicbBitUnits : byte
    {
        eMicbBitUnitGeneral = 0,
        eMicbBitUnitSensors = 1,
        eMicbNumOfBitUnits = 2
    }

    public enum eEstopButtonStatus : byte
    {
        eEstopButtonInvalid = 0,
        eEstopButtonNotPressed = 1,
        eEstopButtonError = 2,
        eEstopButtonDisconnected=3,
        eEstopButtonPressed = 4
    }

    public enum eEstopButtonFieldMapping : byte
    {
        eEstopButtonMicLeft = 0,
        eEstopButtonMicRight = 1,
        eEstopButtonMc = 2,
        eEstopButtonRc = 3,
        eEstopNumButton = 4
    }

    public enum eEstopStatusAdditionalInfo : int
    {
        eEstopStatusMicbWD = 0,                    // 0 - ok , 1 - error
        eEstopStatusMicbRequest,                   // 0 - no request , 1 - request estop
        eEstopStatusMocbRequest,                   // 0 - no request , 1 - request estop
        eEstopStatusMcRequest,                     // 0 - no request , 1 - request estop
        eEstopStatusRcRequest,                     // 0 - no request , 1 - request estop
        eEstopStatusActiveSignal,                  // status if Close Estop signal is active ,0 - no signal
        eEstopStatusActiveSignalPulse,             // status if SW send A if Close Estop signal
        eEstopStatusButtonMicLeftNO1,              // MIC left button Normally open status
        eEstopStatusButtonMicLeftNC1,              // MIC left button Normally Closed status
        eEstopStatusButtonMicRightNO1,             // MIC Right button Normally open status
        eEstopStatusButtonMicRightNC1,             // MIC Right button Normally Closed status
        eEstopStatusButtonMcNO1,                   // MC button Normally open status
        eEstopStatusButtonMcNC1,                   // MC button Normally Closed status
        eEstopStatusButtonRcNO1,                   // RC button Normally open status
        eEstopStatusButtonRcNC1,                   // RC button Normally Closed status
        eEstopRcbLinesOk,                          // RC lines Fault indication , if 1 - ok , 0 - fault
        eEstopScbLinesOk                           // MC lines Fault indication , if 1 - ok , 0 - fault
    }
}
