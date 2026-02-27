using Microsoft.AspNetCore.Components;
using MSGS;

namespace CardModel
{
    public class UDPStatus
    {
        public decimal id { get; set; }
        public string? title { get; set; }
        public bool IsRounded { get; set; }
        public bool MainBgImg { get; set; }
        public string? icon { get; set; }
        public string? svg { get; set; }
        public string? iconclass { get; set; }
        public string? value { get; set; }
        public string? status { get; set; }
        public string? statusclass { get; set; }
        public string? statusdata { get; set; }
        public string? statusicon { get; set; }
        public string? color { get; set; }
        public bool IsIncreased { get; set; }
        public string? percentage { get; set; }
        public string? Badge { get; set; }
        public string? BadgeValue { get; set; }
    };

    public class TableText
    {
        public string Title { get; set; } = string.Empty;
        public string? HeaderClass { get; set; } = string.Empty;
    }
    public class FlaData
    {
        public double EncoderPosition { get; set; }
        public double CurrentR { get; set; }
        public double Status { get; set; }
        public ulong ErrorCode { get; set; }
    }
    public class VoltageData
    {
        public double RCB { get; set; }
        public double _4MBLeft { get; set; }
        public double _5MBLeft { get; set; }
        public double EEFLeft { get; set; }
        public double _4MBRight { get; set; }
        public double _5MBRight { get; set; }
        public double EEFRight { get; set; }
    }

    public class VersionItem
    {
        public String? Name { get; set; }
        public String? Expected { get; set; }
        public String? Received { get; set; }
    }

    public class VersionTable
    {
        public String? AgentVersion { get; set; }
        public String? IDDVersion { get; set; }
        public String? MICCBVersion { get; set; }
        public String? MOCBVersion { get; set; }
        public String? RCVersion { get; set; }
        public String? MCVersion { get; set; }
    }

    public class MicroscopeMotionControlAxis
    {
        public double AbsEnc { get; set; }
        public double MotStep { get; set; }
        public string? AbsPosition { get; set; }
        public string? RelativePosition { get; set; }
    }
    public class MicroscopeMotionControl
    {
        public MicroscopeMotionControlAxis? AxisX { get; set; }
        public MicroscopeMotionControlAxis? AxisY { get; set; }
        public MicroscopeMotionControlAxis? AxisZ { get; set; }
    }

    public class RollAxis
    {
        public double Angle { get; set; }
        public string? AbsAngle { get; set; }
    }
    public class Iris
    {
        public double Diameter { get; set; }
        public double MotStep { get; set; }
        public double AbsEnc { get; set; }
        public string? AbsDia { get; set; }
        public string? RelativeDia { get; set; }
    }

    public class FanInfo
    {
        public long Tacho { get; set; }
        public string? PWM { get; set; }
    }

    public class JoystickInfo
    {
        public double? AbdEncA { get; set; }
        public double? AbdEncB { get; set; }
    }

    public class JoystickForceInfo
    {
        public string? Force { get; set; }
        public double? VcmCurrent { get; set; }
    }

    public class MotorPosition
   {
        public double Position { get; set; }
        public double LimitLow { get; set; }
        public double LimitHigh { get; set; }
    }

    public class MotorInfo
    {
        public MotorPosition PositionAbs { get; set; } = new MotorPosition();
        public MotorPosition PositionInc { get; set; } = new MotorPosition();
        public MotorPosition Decoder { get; set; } = new MotorPosition();
        public string AbsoluteValue { get; set; } = new string("");
        public string RelativeValue { get; set; } = new string("");
        public double BrakesCurrent { get; set; } = 0;
    }

    public class MotorsInfo
    {
        public MotorInfo[] Motors { get; set; } = new MotorInfo[7];
        public bool MotorState { get; set; }
        public string? MotorStatus { get; set; } = new string("");
        public string? SpeedLimit { get; set; } = new string("35");
    }

    public class HeaderDropdownState
    {
        public int CurrentSelect { get; set; }
        public bool Visible { get; set; }
        public bool Enabled { get; set; }
        public string Caption { get; set; } = "";
        public bool ErrorExists { get; set; } = false; // Added to track error state
        public string ErrorTag { get; set; } = ""; // Added to store error tag
        public string Tooltip { get; set; } = ""; 
    }

    public class MotorWizardParams
    {
        public string selectedArm { get; set; } = "";
        public string selectedMotor { get; set; } = "";
        public string selectedAlgo { get; set; } = "";
        public string selectedPressure { get; set; } = "";
        public string selectedVelocity { get; set; } = "";
        public string selectedPWM { get; set; } = "";
        public string selectedTimeout { get; set; } = "";
    }
    public class ButtonSpec {
        public string Caption { get; set; } = "";
        public Func<Task>? OnClick { get; set; } = default!;

        public bool IsOK = true;
    }

    public class EStopInfo
    {
        public string Header { get; set; } = "";
        public string ParameterName { get; set; } = "";
        public string Value { get; set; } = "";
        public string IconSet { get; set; } = "";
    }

    public class DataTableInfo
    {
        public string Header { get; set; } = "";
        public string Value { get; set; } = "";
        public string ScaleName { get; set; } = "";
    }
}
