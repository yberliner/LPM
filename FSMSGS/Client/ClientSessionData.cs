using System;
using System.Reflection.Metadata.Ecma335;
using static MSGS.agentData;

namespace MSGS
{
    public class ClientSessionData
    {
        private readonly AgentsRepository _agents;
        private string _agentName = string.Empty;
        private EMBVersionStorage _emb_versions;


        public ClientSessionData(AgentsRepository agents, EMBVersionStorage emb_versions)
        {
            _agents = agents;
            _emb_versions = emb_versions;
        }

        public string AgentName
        {
            get
            {
                if (_agentName == string.Empty)
                {
                    //Console.WriteLine("⚠️ AgentName is not set yet. Returning empty string.");
                }
                return _agentName;
            }
            set => _agentName = value;
        }

        /// <summary>
        /// Get the underlying agentData for this session.
        /// </summary>
        private agentData? Agent => _agents.GetClientAgentData(AgentName);

        public MicB2VC_Status micb_periodic_status
        {
            get => Agent != null ? Agent.micb_periodic_status : default;
            set
            {
                if (Agent != null)
                {
                    Agent.micb_periodic_status = value;
                }
            }
        }

        public VC2MicB_Control micb_periodic_msg
        {
            //get => Agent != null ? Agent.micb_periodic_msg : default;
            get => Agent != null ? MSGHelper.UpdateEmbededVersion(Agent.micb_periodic_msg, DevicesScreen.MICB, _emb_versions, AgentName) : default;
            set
            {
                if (Agent != null)
                {
                    Agent.micb_periodic_msg = value;
                }
            }
        }

        public VC2MicB_Init micb_init
        {
            get => Agent != null ? Agent.micb_init : default;
            set
            {
                if (Agent != null)
                {
                    Agent.micb_init = value;
                }
            }
        }

        public MicB2VC_Init micb_init_reply
        {
            get => Agent != null ? Agent.micb_init_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.micb_init_reply = value;
                }
            }
        }

        public SMicBMetryMsg micb_metry_reply
        {
            get => Agent != null ? Agent.micb_metry_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.micb_metry_reply = value;
                }
            }
        }

        public MocB2VC_Status mocb_periodic_status
        {
            get => Agent != null ? Agent.mocb_periodic_status : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mocb_periodic_status = value;
                }
            }
        }

        public VC2MocB_Control mocb_periodic_msg
        {
            //get => Agent != null ? Agent.mocb_periodic_msg : default;
            get => Agent != null ? MSGHelper.UpdateEmbededVersion(Agent.mocb_periodic_msg, DevicesScreen.MOCB, _emb_versions, AgentName) : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mocb_periodic_msg = value;
                }
            }
        }

        public VC2MocB_Init mocb_init
        {
            get => Agent != null ? Agent.mocb_init : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mocb_init = value;
                }
            }
        }

        public MocB2VC_Init mocb_init_reply
        {
            get => Agent != null ? Agent.mocb_init_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mocb_init_reply = value;
                }
            }
        }

        public SMocBMetryMsg mocb_metry_reply
        {
            get => Agent != null ? Agent.mocb_metry_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mocb_metry_reply = value;
                }
            }
        }

        public RC2RKS_Status rc_periodic_status
        {
            get => Agent != null ? Agent.rc_periodic_status : default;
            set
            {
                if (Agent != null)
                {
                    Agent.rc_periodic_status = value;
                }
            }
        }

        public RKS2RC_Control rc_periodic_msg
        {
            get => Agent != null ? MSGHelper.UpdateEmbededVersion(Agent.rc_periodic_msg, DevicesScreen.RC, _emb_versions, AgentName) : default;
            set
            {
                if (Agent != null)
                {
                    Agent.rc_periodic_msg = value;
                }
            }
        }

        public RKS2RC_Init rc_init
        {
            get => Agent != null ? Agent.rc_init : default;
            set
            {
                if (Agent != null)
                {
                    Agent.rc_init = value;
                }
            }
        }

        public RC2RKS_Init rc_init_reply
        {
            get => Agent != null ? Agent.rc_init_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.rc_init_reply = value;
                }
            }
        }

        public SRcControlMetry rc_metry_oper_reply
        {
            get => Agent != null ? Agent.rc_metry_oper_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.rc_metry_oper_reply = value;
                }
            }
        }

        public SRcDebugMetry rc_metry_init_reply
        {
            get => Agent != null ? Agent.rc_metry_init_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.rc_metry_init_reply = value;
                }
            }
        }

        public MC2RKS_Status mc_periodic_status
        {
            get => Agent != null ? Agent.mc_periodic_status : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mc_periodic_status = value;
                }
            }
        }

        public RKS2MC_Control mc_periodic_msg
        {
            //get => Agent != null ? Agent.mc_periodic_msg : default;
            get => Agent != null ? MSGHelper.UpdateEmbededVersion(Agent.mc_periodic_msg, DevicesScreen.MC_FAST, _emb_versions, AgentName) : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mc_periodic_msg = value;
                }
            }
        }

        public RKS2MC_Init mc_init
        {
            get => Agent != null ? Agent.mc_init : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mc_init = value;
                }
            }
        }

        public MC2RKS_Init mc_init_reply
        {
            get => Agent != null ? Agent.mc_init_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mc_init_reply = value;
                }
            }
        }

        public SMcFastDiagnostics mc_metry_fast_reply
        {
            get => Agent != null ? Agent.mc_metry_fast_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mc_metry_fast_reply = value;
                }
            }
        }

        public SMcDisgnostics mc_metry_slow_reply
        {
            get => Agent != null ? Agent.mc_metry_slow_reply : default;
            set
            {
                if (Agent != null)
                {
                    Agent.mc_metry_slow_reply = value;
                }
            }
        }

        public MotionScriptEngine? motion_engine
        {
            get => Agent?.motion_engine;
            set
            {
                if (Agent != null)
                {
                    Agent.motion_engine = value;
                }
            }
        }

        public GeneralSaver? generalSaver
        {
            get => Agent?.generalSaver;
            set
            {
                if (Agent != null)
                {
                    Agent.generalSaver = value;
                }
            }
        }

        public BitConfigManager? bitConfigManager
        {
            get => Agent?.bitConfigManager;
            set
            {
                if (Agent != null)
                {
                    Agent.bitConfigManager = value;
                }
            }
        }

        public void RegisterBitConfigCallBack(BitConfigStatusReceivedHandler handler)
        {
           Agent?.RegisterBitConfigStatusCallback(handler);
        }

        public void UnregisterBitConfigStatusCallback(BitConfigStatusReceivedHandler handler)
        {
            Agent?.UnregisterBitConfigStatusCallback(handler);
        }
    }
}
