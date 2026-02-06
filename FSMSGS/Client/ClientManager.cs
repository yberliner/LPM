using HDF.PInvoke;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static MSGS.cContinuousAxisData;
#nullable disable
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.

namespace MSGS
{
    /// <summary>
    /// Scoped class. Holds the client essential data
    /// </summary>
    public class ClientManager : IDisposable
    {
        //private Timer _updateAnActiveClientTimer = null;
        private bool _disposed;
        
        //Singleton params
        private readonly CommRepository _commRepository;
        private readonly OutgoingMsgsManager _outMsgsManager;
        private readonly AgentsRepository _agentsRepository;

        //Scoped params
        private readonly ClientSessionData _data;
        public ClientSessionData UserData => _data;

        public ClientManager(ClientSessionData sessionData, TCPEngine tcpEngine,
            CommRepository commRepository, 
            OutgoingMsgsManager outMsgsManager, AgentsRepository agentsRepository)
        {
            _data = sessionData;
            _commRepository = commRepository;
            _outMsgsManager = outMsgsManager;
            _agentsRepository = agentsRepository;
        }

        public void Dispose()
        {
            if (_disposed) return;

            Console.WriteLine("Disposing ClientManager...");
            
            UserData.AgentName = string.Empty;
            _disposed = true;

            //_updateAnActiveClientTimer?.Dispose();
            //_updateAnActiveClientTimer = null;

            GC.SuppressFinalize(this); // prevent unnecessary finalization
        }

        public void OnSessionEnd()
        {
            Console.WriteLine($"OnSessionEnd. Nulling the agent");
            //Stop the timer
            
            UserData.AgentName = string.Empty;

            //_updateAnActiveClientTimer?.Dispose();
            //_updateAnActiveClientTimer = null;

        }
        public void OnSessionStart(string? agentName)
        {
            Console.WriteLine($"OnSessionStart - Agent name: {agentName}");
            InitClientManager(agentName);
        }

        public void ClientRefresh(string agentName)
        {
            Console.WriteLine($"ClientRefresh - Agent name: {agentName}");
            InitClientManager(agentName);
        }
        private bool InitClientManager(string agentName)
        {
            if (agentName == null)
            {
                Console.WriteLine($"⚠️ OnSessionStart - Agent name is null or empty!");
                Debug.Assert(false, "Agent name was null or empty!");
                return false;
            }
            UserData.AgentName = agentName;

            //StartPingTimer();
            return true;
        }

        public void SendServerForwardCmdGeneric<T>(int device, int deviceTypeMsg,
            int start_stop, ref T deviceStruct, bool updateVersion = true) where T : struct
        {
            _outMsgsManager.SendServerCmdGeneric(device, deviceTypeMsg,
                start_stop, ref deviceStruct, UserData.AgentName, Cmd.Forward, updateVersion);
        }

        public void StartGeneralSaver()
        {
            if (UserData.generalSaver != null)
            {
                UserData.generalSaver.Dispose();
            }
            UserData.generalSaver = new GeneralSaver(UserData.AgentName, _agentsRepository);
        }
        public void StopGeneralSaver()
        {
            if (UserData.generalSaver != null)
            {
                UserData.generalSaver.Dispose();
                UserData.generalSaver = null;
            }
        }
        
        public void StartMotionScriptHandling(int deviceTypeMsg, string fileName, int num_of_loops, 
            bool record_status, string sub_folder, string jsonTextUserVariables = "")
        {
            List<byte[]>? msgs = SendMotionScriptMsg(deviceTypeMsg, fileName, num_of_loops, 
                record_status, sub_folder, jsonTextUserVariables);
        }

        public void CancelScript()
        {
            Console.WriteLine("Client manager CancelScript called.");

            byte[]? last_rc_msg = UserData.motion_engine?.OnCancel();
            if (last_rc_msg != null)
            {
                Console.WriteLine("Sending last RC message after cancel script.");
                RKS2RC_Control rc_msg = MSGHelper.ByteArrayToStruct<RKS2RC_Control>(last_rc_msg);
                SendServerForwardCmdGeneric((int)DevicesScreen.RC, 2, 1, ref rc_msg);
            }
            UserData.motion_engine = null;
            Console.WriteLine("Client manager Motion script End.");
        }

        
        private List<byte[]>? SendMotionScriptMsg(
            int deviceTypeMsg, 
            string motionScriptFileName,
            int num_of_loops,
            bool recordStatus,
            string sub_folder,
            string jsonTextUserVariables)
        {
            try
            {
                if (string.IsNullOrEmpty(UserData.AgentName))
                {
                    Console.WriteLine($"Agent name was null or empty!");
                    Debug.Assert(false, "Agent name was null or empty!");
                    //UserData.AgentName = "DUMMY - FOR TEST";
                }


                MotionScriptFactory factory = new(motionScriptFileName, num_of_loops);

                RKS2RC_Control rc_periodic_msg = UserData.rc_periodic_msg;

                //make sure the enabled is on for RC.
                for (int i = 0; i < (int)(int)eRcSubsystems.eRcNumOfSubsystems; i++)
                {
                    rc_periodic_msg.subsystem_cmd[i] = eSysState.eActive; 
                    rc_periodic_msg.reset_errors[i] = eModuleErrorState.eModuleErrorStateClear; //for inetgration
                }

                //also make sure the default speed is fast mode.
                rc_periodic_msg.manipulator_cmd_left.slow_mode = eRcOperationMode.eRcOperationModeFast;
                rc_periodic_msg.manipulator_cmd_right.slow_mode = eRcOperationMode.eRcOperationModeFast;

                var (rc_msgs, id) = LoadAndSend(factory, rc_periodic_msg,
                    DevicesScreen.RC, deviceTypeMsg);

                UserData.motion_engine = new MotionScriptEngine(motionScriptFileName, rc_msgs,
                    id, recordStatus, sub_folder, UserData.AgentName, factory.NumOfGoToMsgs, jsonTextUserVariables);

                if (!factory._is_freq_script)
                {
                    LoadAndSend(factory, UserData.micb_periodic_msg,
                        DevicesScreen.MICB, deviceTypeMsg);
                    LoadAndSend(factory, UserData.mocb_periodic_msg,
                        DevicesScreen.MOCB, deviceTypeMsg);
                    LoadAndSend(factory, UserData.mc_periodic_msg,
                        DevicesScreen.MC_FAST, deviceTypeMsg);
                }
            }
            catch(Exception e)
            {
                Console.WriteLine($"Exception in running a script: {motionScriptFileName}. Exception: {e.ToString()}");
            }
            return null;
        }

        private (List<byte[]> Messages, int ForwardId) LoadAndSend<T>(
            MotionScriptFactory factory,
            T periodic_msg, 
            DevicesScreen device, 
            int deviceTypeMsg) where T : struct
        {
            var forwardData = new DeviceForwardData();
            var devices_msgs = new List<byte[]>();

            var tcp_msg = factory.CreateFromFile(
                (int)device, deviceTypeMsg, ref forwardData, devices_msgs,
                periodic_msg, UserData);

            if (tcp_msg != null)
            {
                _outMsgsManager.SendMsg(UserData.AgentName, ref tcp_msg);
            }
            return (devices_msgs, forwardData.id);
        }

        public void GoToEngines(ref RKS2RC_Control rcCmd, int speed_limit)
        {
            var forwardData = new DeviceForwardData();

            List<byte[]> goto_msgs = new GoToEngine().Generate(ref rcCmd, UserData.rc_periodic_status, false, speed_limit);

            byte[] tcp_msg = MessageCreator.CreateForwardMessage((int)DevicesScreen.RC, 2,
                ref goto_msgs, out forwardData, Cmd.Forward, 1);

            if (tcp_msg != null)
            {
                _outMsgsManager.SendMsg(UserData.AgentName, ref tcp_msg);
            }
        }

        public bool ResetErrors(Func<Task>? onCompletedCallback, bool haltEngine = false)
        {
            Console.WriteLine($"ResetErrors Agent: {UserData.AgentName}");

            if (string.IsNullOrEmpty(UserData.AgentName) ||
                    _agentsRepository.GetClientAgentData(UserData.AgentName) == null)
            {
                Console.WriteLine($"ResetErrors Error! Agent does not exists");
                return false;
            }

            if (haltEngine)
            {
                Console.WriteLine("Halt engine requested. Resetting errors and stopping robot movement.");
                //make sure robot will not move after reser error.
                for (int i = 0; i < 7; i++)
                {
                    UserData.rc_periodic_msg.manipulator_cmd_left.target.poseArr[i] =
                        UserData.rc_periodic_status.manipulator_status[0].pose.poseArr[i];

                    UserData.rc_periodic_msg.manipulator_cmd_right.target.poseArr[i] =
                        UserData.rc_periodic_status.manipulator_status[1].pose.poseArr[i];
                }
            }
            Console.WriteLine("Resetting errors for all devices.");
            ResetErrorManager resetErrorManager = new ResetErrorManager(
            _agentsRepository, UserData.AgentName, 
            UserData.micb_periodic_status, UserData, this, onCompletedCallback);

            return true;
        }

        public bool EstopDisconnect(Func<Task>? onCompletedCallback)
        {
            return SetMicbEstopState(eEstopStateCmd.eEstopStateSysDisconnect, onCompletedCallback);
        }

        public bool EstopOperational(Func<Task>? onCompletedCallback)
        {
            return SetMicbEstopState(eEstopStateCmd.eEstopStateSysOperational, onCompletedCallback);
        }

        public bool SetMicbEstopState(eEstopStateCmd state, Func<Task>? onCompletedCallback,
            bool sendMicBMsg = true, // Optional parameter to control sending MicB message
            bool sendRCBMsg = true, // Optional parameter to control sending RCB message
            bool sendMCBMsg = true) // Optional parameter to control sending MCB message)
        {
            Console.WriteLine($"Setting Estop to: {state}. Agent: {UserData.AgentName}");
            if (string.IsNullOrEmpty(UserData.AgentName) ||
                    _agentsRepository.GetClientAgentData(UserData.AgentName) == null)
            {
                Console.WriteLine($"SetMicbEstopState Error! Agent does not exists");
                return false;
            }
            EstopRequestManager estopReqManager = new EstopRequestManager(
            _agentsRepository, UserData.AgentName,
            UserData, this, state, onCompletedCallback, 
            sendMicBMsg, sendRCBMsg, sendMCBMsg);

            return true;
        }

        public void OnDeviceClicked(DevicesScreen deviceScreen, bool isInit)
        {
            Console.WriteLine($"Device clicked: {deviceScreen}. IsInit: {isInit}");

            bool handled = deviceScreen switch
            {
                DevicesScreen.RC => HandleDevice(
                    DevicesScreen.RC,
                    () => UserData.rc_init, 1,
                    () => UserData.rc_periodic_msg, 2),
                DevicesScreen.MICB => HandleDevice(
                    DevicesScreen.MICB,
                    () => UserData.micb_init, 1,
                    () => UserData.micb_periodic_msg, 2),
                DevicesScreen.MOCB => HandleDevice(
                    DevicesScreen.MOCB,
                    () => UserData.mocb_init, 1,
                    () => UserData.mocb_periodic_msg, 2),
                DevicesScreen.MC_FAST => HandleDevice(
                    DevicesScreen.MC_FAST,
                    () => UserData.mc_init, 1,
                    () => UserData.mc_periodic_msg, 2),
                _ => false
            };

            if (!handled)
                Console.WriteLine($"Unknown device screen: {deviceScreen}");

            // Local function to handle the device generically
            bool HandleDevice<TInit, TPeriodic>(
                DevicesScreen screen,
                Func<TInit> getInit, int initMsg,
                Func<TPeriodic> getPeriodic, int periodicMsg)
                where TInit : struct
                where TPeriodic : struct
            {
                if (isInit)
                {
                    var init = getInit();
                    SendServerForwardCmdGeneric((int)screen, initMsg, 1, ref init);
                }
                else
                {
                    var periodic = getPeriodic();
                    SendServerForwardCmdGeneric((int)screen, periodicMsg, 1, ref periodic);
                }
                return true;
            }
        }
        public void LogOff()
        {
            _outMsgsManager.LogCmd(UserData.AgentName, Cmd.LogOFF);
        }
        public void LogDefaultOn()
        {
            _outMsgsManager.LogCmd(UserData.AgentName, Cmd.LogDefaultOn);
        }
        public void LogCustomOn()
        {
            _outMsgsManager.LogCmd(UserData.AgentName, Cmd.LogCustomOn);
        }
        public void CloseAgent()
        {
            _outMsgsManager.CloseAgent(UserData.AgentName);
        }
        //private void StartPingTimer()
        //{
        //    return;
        //    if (_updateAnActiveClientTimer == null)
        //    {
        //        _updateAnActiveClientTimer = new Timer(_ =>
        //        {
        //            if (_disposed || _updateAnActiveClientTimer==null) return;

        //            try
        //            {
        //                if (!string.IsNullOrEmpty(UserData.AgentName))
        //                {

        //                    _commRepository.ClientBrowserIsAlive(UserData.AgentName);
        //                }
        //                else
        //                {
        //                    Console.WriteLine("Error: _updateAnActiveClientTimer - Agent name is null");
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                Console.WriteLine($"⚠️ _updateAnActiveClientTimer error: {ex.Message}");
        //            }
        //        }, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3)); // first 5 secondss, then every 3 seconds
        //    }
        //}

    }
}
