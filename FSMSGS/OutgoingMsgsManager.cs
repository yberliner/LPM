using FSMSGS;
using Microsoft.Extensions.Options;
using MSGS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    /// <summary>
    /// Singleton class for managing outgoing msgs
    /// </summary>
    public class OutgoingMsgsManager : IDisposable
    {
        private readonly bool _isYanivLocal;

        private bool _disposed;
        //private Timer? _pingTimer;
        private readonly TCPEngine _tcpEngine;
        private readonly AgentsRepository _agents;
        private readonly CommRepository _commRepository;
        private readonly EMBVersionStorage _embVersionStorage;
        public enum MsgType { Init, Oper };

        public OutgoingMsgsManager(TCPEngine tcpEngine, 
            AgentsRepository agents,
            CommRepository commRepository,
            IOptions<TCPSettings> tcpSettingsOptions,
            EMBVersionStorage embVersionStorage )
        {
            _agents = agents;
            _tcpEngine = tcpEngine;
            _commRepository = commRepository;
            _isYanivLocal = tcpSettingsOptions.Value.Yaniv_Local;
            _embVersionStorage = embVersionStorage;
        }
        public void Start()
        {             
            Console.WriteLine("OutgoingMsgsManager started.");
            _commRepository.OnAgentAdded += agentAdded;
        }

        public void agentAdded(string agentName, MsgType msgType)
        {
            Console.WriteLine($"Agent {agentName} added - Sending startup msgs. Msg Type: {msgType}");
            // When a new agent is added, send the startup messages
            SendStartupMessages(agentName, msgType);
        }
        public void Dispose()
        {
            if (_disposed) return;

            _commRepository.OnAgentAdded -= (agentName, msgType) =>
            {
                // When a new agent is added, send the startup messages
                SendStartupMessages(agentName, msgType);
            };

            //_pingTimer?.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this); // prevent unnecessary finalization
        }

        public void SendServerCmdGeneric<T>(int device, int deviceTypeMsg,
                int start_stop, ref T deviceStruct, 
                string agentName, Cmd command, bool updateVersion = true) where T : struct
        {
            byte[] device_msg = MSGHelper.StructureToByteArray(deviceStruct);
            if (updateVersion)
            {
                T updateVersionDevice = MSGHelper.UpdateEmbededVersion(deviceStruct, (DevicesScreen)device, _embVersionStorage, agentName);
                device_msg = MSGHelper.StructureToByteArray(updateVersionDevice);
            }
            //byte[] device_msg = MSGHelper.StructureToByteArray(deviceStruct);

            ////get the old version from the msg
            //byte[] slice_version = device_msg.Skip(1).Take(3).ToArray();
            //cidd_version old_version = new cidd_version(slice_version);

            //cidd_version new_version = _embVersionStorage.GetVersion(agentName, (DevicesScreen)device, old_version);

            //if (updateVersion)
            //{
            //    //Copy if needed and Do logging.
            //    if (old_version.VersionMajor != new_version.VersionMajor ||
            //        old_version.VersionMinor != new_version.VersionMinor ||
            //        old_version.VersionPatch != new_version.VersionPatch)
            //    {
            //        //copy the new version into the struct
            //        Array.Copy(MSGHelper.StructureToByteArray(new_version), 0, device_msg, 1, 3);

            //        Console.WriteLine($"Version Changed!! Agent: {agentName}. Device: {(DevicesScreen)device}. " +
            //            $"Version updated from {old_version} to {new_version}. Before sending to agent.");
            //    }
            //}

            List<byte[]> msgs = new() { device_msg };

            byte[] msg = MessageCreator.CreateForwardMessage(device, deviceTypeMsg,
                ref msgs, data: out DeviceForwardData data, command);

            SendMsg(agentName, ref msg);
        }


        public void SendMsg(string agentName, ref byte[] msg)
        {
            agentData? agent = _agents.GetClientAgentData(agentName);
            if (agent == null)
            {
                Console.WriteLine($"⚠️ Agent {agentName} is null at SendMsg.");
                return;
            }
            //if (agent.startup_msg_sent == false)
            //{
            //    agent.startup_msg_sent = true;
            //    SendStartupMessages(agentName);
            //}
            //send via tcp engine. It is singleton so it will protect also the rare case
            //where 2 clients connect to the same agent.
            _tcpEngine.SendMsg(ref msg, agentName);
        }

        public void SendStartupMessages(string agentName, MsgType msgType)
        {
            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MICB, 1, "100.0.0.173", //MICB
                (int)E_FR_PORTS.eVC_TO_MIC_PORT, (int)E_FR_PORTS.eMIC_TO_VC_PORT, 16_666);

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MOCB, 1, "100.0.0.171", //MOCB
                            (int)E_FR_PORTS.eVC_TO_MOCB_PORT, (int)E_FR_PORTS.eMOCB_TO_VC_PORT, 10_000);

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.RC, 1, "100.0.0.178", //RC
                            (int)E_FR_PORTS.eVC_TO_RC_PORT, (int)E_FR_PORTS.eRC_TO_VC_PORT, 1_000_000 / 480);

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MC_FAST, 1, "100.0.0.179", //MC_FAST-FAST
                            (int)E_FR_PORTS.eVC_TO_MC_PORT, (int)E_FR_PORTS.eMC_TO_VC_PORT, 1_000_000 / 480);

            //METRY
            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MICB_METRY, 1, "100.0.0.173", //MICB_METRY
                            (int)0, (int)E_FR_PORTS.eMICB_TO_METRY_PORT, 1_000_000_000); // One way to VC only

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MOCB_METRY, 1, "100.0.0.171", //MOCB_METRY
                            (int)0, (int)E_FR_PORTS.eMOCB_TO_METRY_PORT, 1_000_000_000); // One way to VC only

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.RC_METRY, 1, "100.0.0.178", //RC_METRY
                            (int)0, (int)E_FR_PORTS.eRC_TO_METRY_PORT, 1_000_000_000); // One way to VC only

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MC_FAST_METRY, 1, "100.0.0.179", //MC_FAST_METRY
                            (int)0, (int)E_FR_PORTS.eMC_FAST_TO_METRY_PORT, 1_000_000_000); // One way to VC only

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MC_SLOW_METRY, 1, "100.0.0.174", //MC_SLOW_METRY
                            (int)0, (int)E_FR_PORTS.eMC_SLOW_TO_METRY_PORT, 1_000_000_000); // One way to VC only

            //CONFIG
            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MICB_CONFIG, 1, "100.0.0.173", //MICB
                (int)E_FR_PORTS.eVC_TO_MIC_CONFIG_PORT, (int)E_FR_PORTS.eMIC_TO_VC_CONFIG_PORT, 1_000_000_000);

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MOCB_CONFIG, 1, "100.0.0.171", //MOCB
                            (int)E_FR_PORTS.eVC_TO_MOCB_CONFIG_PORT, (int)E_FR_PORTS.eMOCB_TO_VC_CONFIG_PORT, 1_000_000_000);

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.RC_CONFIG, 1, "100.0.0.178", //RC
                            (int)E_FR_PORTS.eVC_TO_RC_CONFIG_PORT, (int)E_FR_PORTS.eRC_TO_VC_CONFIG_PORT, 1_000_000_000);

            SendServerMsgStartUpForDevice(agentName, (int)MSGS.DevicesScreen.MC_FAST_CONFIG, 1, "100.0.0.179", //MC_FAST-FAST
                            (int)E_FR_PORTS.eVC_TO_MC_CONFIG_PORT, (int)E_FR_PORTS.eMC_TO_VC_CONFIG_PORT, 1_000_000_000);

            SendInitMsgForVersion(agentName, msgType);

        }

        private void SendInitMsgForVersion(string agentName, MsgType msgType)
        {
            Console.WriteLine($"Sending Init Msgs. Agent: {agentName}. type: {msgType}");

            //send the init command for every device.
            agentData? agent_data = _agents.GetClientAgentData(agentName);

            if (agent_data == null)
            {
                Console.WriteLine($"⚠️ Agent {agentName} is null SendInitMsgForVersion.");
                return;
            }
            if (msgType == MsgType.Oper)
            {
                SendServerCmdGeneric((int)DevicesScreen.MICB, 2, 1,
                        ref agent_data.micb_periodic_msg, agent_data.AgentName, Cmd.Forward);

                SendServerCmdGeneric((int)DevicesScreen.MOCB, 2, 1,
                            ref agent_data.mocb_periodic_msg, agent_data.AgentName, Cmd.Forward);

                SendServerCmdGeneric((int)DevicesScreen.RC, 2, 1,
                            ref agent_data.rc_periodic_msg, agent_data.AgentName, Cmd.Forward);

                SendServerCmdGeneric((int)DevicesScreen.MC_FAST, 2, 1,
                            ref agent_data.mc_periodic_msg, agent_data.AgentName, Cmd.Forward);
            }
            else if (msgType == MsgType.Init)
            {
                SendServerCmdGeneric((int)DevicesScreen.MICB, 1, 1,
                        ref agent_data.micb_init, agent_data.AgentName, Cmd.Forward);

                SendServerCmdGeneric((int)DevicesScreen.MOCB, 1, 1,
                            ref agent_data.mocb_init, agent_data.AgentName, Cmd.Forward);

                SendServerCmdGeneric((int)DevicesScreen.RC, 1, 1,
                            ref agent_data.rc_init, agent_data.AgentName, Cmd.Forward);

                SendServerCmdGeneric((int)DevicesScreen.MC_FAST, 1, 1,
                            ref agent_data.mc_init, agent_data.AgentName, Cmd.Forward);
            }
            else
            {
                Console.WriteLine("Outgoing msgs Fatal Error - invalid msgType");
            }
        }

        public void SendServerMsgStartUpForDevice(
            string agentName,
            int device,
            int start_stop,
            string ip,
            int port,
            int listenPort,
            int delay_time_micro_seconds)
        {
            if (_isYanivLocal && Environment.MachineName == "DESKTOP-NR3351E")
            {
                Console.WriteLine($"⚠️ Using local Yaniv IP for testing purposes. IP:{ip}");
                ip = _agents.GetClientHost(agentName)!;  //"10.100.102.5";
                if (ip == null)
                {
                    ip = "10.100.102.8";
                    //ip = "192.168.1.35";
                }
                //ip = "192.168.1.56";
            }
            
            byte[] msg = MessageCreator.CreateInitDeviceMessage(device, start_stop,
                ip, port, listenPort, delay_time_micro_seconds);

            SendMsg(agentName, ref msg);
        }

        internal void LogCmd(string agentName, Cmd logCmd)
        {
            SendAgentCmd(agentName, logCmd);
        }

        internal void CloseAgent(string agentName)
        {
            SendAgentCmd(agentName, Cmd.CloseAgent);
        }

        private void SendAgentCmd(string agentName, Cmd logCmd)
        {
            DeviceMsg device_msg = new()
            {
                device = 0,
                cmd = (int)logCmd
            };
            byte[] device_msg_Bytes = MSGHelper.StructureToByteArray(device_msg);
            SendMsg(agentName, ref device_msg_Bytes);
        }

        
        //public void Start()
        //{
        //    //StartPingTimer();

        //}

        //private void StartPingTimer()
        //{
        //    return;

        //    int index_to_send_get_version = 0;
        //    _pingTimer = new Timer(_ =>
        //    {
        //        SendPindMsgsToAgents();

        //        List<string> activeAgentToServer = _commRepository.GetActiveAgentsByClientsOrDirectToServer(
        //            x => x.LastAgentAccessTime);

        //        //remove the agents that did not response (crash or stopped)
        //        List<string> removed_agents = _commRepository.RemoveOldAgents(
        //            activeAgentToServer);

        //        _agents.DeleteAgents(removed_agents);

        //        if (index_to_send_get_version++ % 60 == 0)
        //        {
        //            SendGetVersion();
        //        }

        //    }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)); // first 5 seconds, then every 10 seconds
        //}


        ///// <summary>
        ///// Send the ping msg to all active agents (access now by a client)
        ///// </summary>
        //private void SendPindMsgsToAgents()
        //{
        //    try
        //    {
        //        List<string> activeClientToAgent = _commRepository.GetActiveAgentsByClientsOrDirectToServer(
        //            x => x.LastClientBrowserAccessTime);

        //        DeviceMsg pingMsg = new() { cmd = (int)Cmd.Ping, data_len = 0 }; // empty struct
        //        byte[] device_msg_Bytes = MSGHelper.StructureToByteArray(pingMsg);

        //        foreach (var agentName in activeClientToAgent)
        //        {
        //            SendMsg(agentName, ref device_msg_Bytes);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"⚠️ Ping error: {ex.Message}");
        //    }
        //}

        //private void SendGetVersion()
        //{
        //    foreach (var agentData in _agents.GetAgents)
        //    {
        //        SendInitMsgForVersion(agentData.AgentName);
        //    }
        //}
    }
}
