using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MSGS
{
    public class agentData
    {
        public agentData()
        {
            for (int side = 0; side < (int)e_sides.eNumOfSides; side++)
            {
                _rc_periodic_status.manipulator_status[side].pose = new cRcPose();
            }
        }
        // MicB
        private MicB2VC_Status _micb_periodic_status = new();
        public MicB2VC_Status micb_periodic_status
        {
            get => _micb_periodic_status;
            set
            {
                _micb_periodic_status = value;
                MicB_Periodic_ReceivedTime = DateTime.Now;
            }
        }
        public DateTime? MicB_Periodic_ReceivedTime { get; private set; }

        public VC2MicB_Control micb_periodic_msg = new();
        public VC2MicB_Init micb_init = new();

        private MicB2VC_Init _micb_init_reply = new();
        public MicB2VC_Init micb_init_reply
        {
            get => _micb_init_reply;
            set
            {
                _micb_init_reply = value;
                MicB_Init_Reply_ReceivedTime = DateTime.Now;
            }
        }
        public DateTime? MicB_Init_Reply_ReceivedTime { get; private set; }

        public SMicBMetryMsg micb_metry_reply = new();

        // MocB
        private MocB2VC_Status _mocb_periodic_status = new();
        public MocB2VC_Status mocb_periodic_status
        {
            get => _mocb_periodic_status;
            set
            {
                _mocb_periodic_status = value;
                MocB_Periodic_ReceivedTime = DateTime.Now;
            }
        }
        public DateTime? MocB_Periodic_ReceivedTime { get; private set; }

        public VC2MocB_Control mocb_periodic_msg = new();
        public VC2MocB_Init mocb_init = new();

        private MocB2VC_Init _mocb_init_reply = new();
        public MocB2VC_Init mocb_init_reply
        {
            get => _mocb_init_reply;
            set
            {
                _mocb_init_reply = value;
                MocB_Init_Reply_ReceivedTime = DateTime.Now;
            }
        }
        public DateTime? MocB_Init_Reply_ReceivedTime { get; private set; }

        public SMocBMetryMsg mocb_metry_reply = new();

        // RC
        private RC2RKS_Status _rc_periodic_status = new();
        public RC2RKS_Status rc_periodic_status
        {
            get => _rc_periodic_status;
            set
            {
                _rc_periodic_status = value;
                Rc_Periodic_ReceivedTime = DateTime.Now;
            }
        }
        public DateTime? Rc_Periodic_ReceivedTime { get; private set; }

        public RKS2RC_Control rc_periodic_msg = new();
        public RKS2RC_Init rc_init = new();

        private RC2RKS_Init _rc_init_reply = new();
        public RC2RKS_Init rc_init_reply
        {
            get => _rc_init_reply;
            set
            {
                _rc_init_reply = value;
                Rc_Init_Reply_ReceivedTime = DateTime.Now;
            }
        }
        public DateTime? Rc_Init_Reply_ReceivedTime { get; private set; }

        public SRcControlMetry rc_metry_oper_reply = new();
        public SRcDebugMetry rc_metry_init_reply = new();

        // MC
        private MC2RKS_Status _mc_periodic_status = new();
        public MC2RKS_Status mc_periodic_status
        {
            get => _mc_periodic_status;
            set
            {
                _mc_periodic_status = value;
                Mc_Periodic_ReceivedTime = DateTime.Now;
            }
        }
        public DateTime? Mc_Periodic_ReceivedTime { get; private set; }

        public RKS2MC_Control mc_periodic_msg = new();
        public RKS2MC_Init mc_init = new();

        private MC2RKS_Init _mc_init_reply = new();
        public MC2RKS_Init mc_init_reply
        {
            get => _mc_init_reply;
            set
            {
                _mc_init_reply = value;
                Mc_Init_Reply_ReceivedTime = DateTime.Now;
            }
        }
        public DateTime? Mc_Init_Reply_ReceivedTime { get; private set; }

        public SMcFastDiagnostics mc_metry_fast_reply = new();
        public SMcDisgnostics mc_metry_slow_reply = new();

        /// <summary>
        /// The JSON file the agent sent on register.
        /// </summary>
        public JsonElement agentJson;

        public string AgentName = string.Empty;

        public MotionScriptEngine? motion_engine = null;

        public bool startup_msg_sent = false;

        public GeneralSaver? generalSaver = null;

        public BitConfigManager? bitConfigManager = null;

        public delegate void BitConfigStatusReceivedHandler(sBitConfigStatus status);
        public event BitConfigStatusReceivedHandler? BitConfigStatusReceived;

        public agentErrorState errorState = new agentErrorState();

        public void RaiseBitConfigStatusReceived(sBitConfigStatus status)
        {
            if (BitConfigStatusReceived == null)
            {
                Console.WriteLine($"BitConfigStatusReceived {AgentName} is null, no handlers registered.");
            }
            BitConfigStatusReceived?.Invoke(status);
        }

        public void RegisterBitConfigStatusCallback(BitConfigStatusReceivedHandler handler)
        {
            BitConfigStatusReceived += handler;
        }

        public void UnregisterBitConfigStatusCallback(BitConfigStatusReceivedHandler handler)
        {
            BitConfigStatusReceived -= handler;
        }
    }


}
