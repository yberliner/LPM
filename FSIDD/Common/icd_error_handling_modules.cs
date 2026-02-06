using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public enum eMcUnits : byte
    {
        eMcUnitsStart = 0,
        eMcUnitsVcm1= 1,
        eMcUnitsVcm2= 2,
        eMcUnitsVcm3= 3,
        eMcUnitsScb= 4,
        eMcUnitsHub= 5,
        eMcUnitsXYZ= 6,
        eMcUnitsX= 11,
        eMcUnitsY= 12,
        eMcUnitsZ= 13,
        eMcUnitsYaw= 14,
        eMcUnitsPitch= 15,
        eMcUnitsRoll= 16,
        eMcUnitsHandle= 17,
        eMcUnitsVc= 18,
        eMcUnitsTable= 21,
        eMcUnits3dScreen= 22,
        eMcUnitsOther= 99,
        eMcMaxNumOfUnits
    }
    //public enum eRcUnits : byte
    //{
    //    eRcUnitsStart = 0,
    //    eRcUnitsSideManager= 0,
    //    eRcUnitsAxis1= 1,
    //    eRcUnitsAxis2= 2,
    //    eRcUnitsAxis3= 3,
    //    eRcUnitsAxis4= 4,
    //    eRcUnitsAxis5= 5,
    //    eRcUnitsAxis6= 6,
    //    eRcUnitsAxis7= 7,
    //    eRcUnits4MotorBoard= 10,
    //    eRcUnitsEndEffectorBoard= 11,
    //    eRcUnitsMotor5Board= 12,
    //    eRcUnitsRcb= 13,
    //    eRcUnitsFla= 16,
    //    eRcUnitsOther= 99,
    //    eRcMaxNumOfUnits
    //}

    public enum eMwsUnits : byte
    {
        eMwsUnitsMicbGeneral = 0,
        eMwsUnitsMicbSensors= 1,

        eMwsUnitsLedCoaxL= 0,
        eMwsUnitsLedCoaxR= 1,
        eMwsUnitsLedParax= 2,
        eMwsUnitsLedTracking= 3,
        eMwsUnitsMocbSensors= 4,
        eMwsUnitsMocbGeneral= 5,
        eMwsUnitsAxisRoll= 6,
        eMwsUnitsIris= 7,

        eMwsUnitsAxisX= 21,
        eMwsUnitsAxisY= 22,
        eMwsUnitsAxisZ= 23,
        eMwsUnitsOther= 99,
        eMwsMaxNumOfUnits
    }
}
