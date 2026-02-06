using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;




// definitions available at https://docs.google.com/spreadsheets/d/1il1LJnLz8hIi49iyViLMIkDCT7oreKqPjvV-Sy5iMEM/edit?usp=sharing

    namespace MSGS
{
        public static class ICD_ADDRESSES
        {
            public const int _FORSIGHT_ROBOTICS_IDD_IP_BYTE_0 = 100;
            public const int _FORSIGHT_ROBOTICS_IDD_IP_BYTE_1 = 0;
            public const int _FORSIGHT_ROBOTICS_IDD_IP_BYTE_2 = 0;
             // addintes
            public const int _FORSIGHT_ROBOTICS_IDD_MOCB_IP_BYTE_3 = 171;
            public const int _FORSIGHT_ROBOTICS_IDD_MICB_SLOW_IP_BYTE_3 = 172;
            public const int _FORSIGHT_ROBOTICS_IDD_MICB_IP_BYTE_3 = 173;
            public const int _FORSIGHT_ROBOTICS_IDD_RC_IP_BYTE_3 = 178;
            public const int _FORSIGHT_ROBOTICS_IDD_RC_M4_IP_BYTE_3 = 175;
            public const int _FORSIGHT_ROBOTICS_IDD_MCFAST_IP_BYTE_3 = 179;
            public const int _FORSIGHT_ROBOTICS_IDD_MCSLOW_METRY_IP_BYTE_3 = 174;
            public const int _FORSIGHT_ROBOTICS_IDD_METRY_IP_BYTE_3 = 170;
            public const int _FORSIGHT_ROBOTICS_IDD_VC_IP_BYTE_3 = 177;
        }
        public enum E_FR_MAC_ADDRESSES 
        {
            E_MC_MAC_ADDRESS_BYTE_5 = 0xFD,
            E_MCC_MAC_ADDRESS_BYTE_5 = 0xFB,
            E_RC_MAC_ADDRESS_BYTE_5 = 0xFC,
            E_MICB_MAC_ADDRESS_BYTE_5 = 0xFA,
            E_MICB_SLOW_MAC_ADDRESS_BYTE_5 = 0xF9,
            E_MOCB_MAC_ADDRESS_BYTE_5 = 0xF8,
            E_RC_M4_MAC_ADDRESS_BYTE_5 = 0xBC
        };

        public enum E_FR_IP_ADDRESSES
        {
            eIP_BYTE_0 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_IP_BYTE_0,
            eIP_BYTE_1 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_IP_BYTE_1,
            eIP_BYTE_2 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_IP_BYTE_2,
            eMICB_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_MICB_IP_BYTE_3,
            eMICB_SLOW_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_MICB_SLOW_IP_BYTE_3,
            eMOCB_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_MOCB_IP_BYTE_3,
            eRC_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_RC_IP_BYTE_3,
            eRC_M4_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_RC_M4_IP_BYTE_3,
            eMCFAST_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_MCFAST_IP_BYTE_3,
            eMCSLOW_METRY_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_MCSLOW_METRY_IP_BYTE_3,
            eMETRY_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_METRY_IP_BYTE_3,
            eRKS_IP_BYTE_4 = ICD_ADDRESSES._FORSIGHT_ROBOTICS_IDD_VC_IP_BYTE_3
        };

        public enum E_FR_PORTS
        {
            eRC_TO_METRY_PORT = 8000,
            eMC_FAST_TO_METRY_PORT = 8010,
            eMC_SLOW_TO_METRY_PORT = 8011,
            eMICB_TO_METRY_PORT = 8020,
            eMOCB_TO_METRY_PORT = 8030,
            eVC_TO_MC_PORT = 8001,
            eVC_TO_RC_PORT = 8002,
            eRC_TO_VC_PORT = 8003,
            eVC_TO_MIC_PORT = 8007,
            eMC_TO_VC_PORT = 8013,
            eMIC_TO_VC_PORT = 8015,
            eMIC_SLOW_MERTY_PORT = 8016,
            eMOCB_TO_VC_PORT = 8017,
            eVC_TO_MOCB_PORT = 8008,
            eMICB_VC_LOG_PORTS = 8881,
            eMOCB_VC_LOG_PORTS = 8882,
            eRC_VC_LOG_PORTS = 8883,
            eMC_VC_LOG_PORTS = 8884,
            eMC_SLOW_VC_LOG_PORTS = 8885,
            eVC_TO_RC_CONFIG_PORT = 8100,
            eVC_TO_MC_CONFIG_PORT = 8101,
            eVC_TO_MIC_CONFIG_PORT = 8102,
            eVC_TO_MOCB_CONFIG_PORT = 8103,
            eRC_TO_VC_CONFIG_PORT = 8200,
            eMC_TO_VC_CONFIG_PORT = 8201,
            eMIC_TO_VC_CONFIG_PORT = 8202,
            eMOCB_TO_VC_CONFIG_PORT = 8203,
        };
    };

    // no code after this line

