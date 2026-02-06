using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class EstopRequestManager
    {
        private readonly ClientManager _manager;
        private readonly AgentsRepository _agentRepository;
        ClientSessionData _UserData;
        private readonly string _agentName;
       
        private int _numOfMsgsReceived = 0;
        public bool isDisposed { get; private set; } = false;

        private System.Timers.Timer? refreshTimer;

        private readonly Stopwatch _lifetimeStopwatch = new Stopwatch();
        private readonly Guid _instanceId = Guid.NewGuid();

        private Func<Task>? _onCompletedCallback; // Add a field for the callback
        bool _sendMicBMsg;
        bool _sendRCBMsg;
        bool _sendMCBMsg;

        public EstopRequestManager(
            AgentsRepository agentRepository, 
            string agentName, 
            ClientSessionData UserData,
            ClientManager manager,
            eEstopStateCmd start_cmd,
            Func<Task>? onCompletedCallback = null, // Accept callback in constructor
            bool sendMicBMsg = true, // Optional parameter to control sending MicB message
            bool sendRCBMsg = true, // Optional parameter to control sending RCB message
            bool sendMCBMsg = true // Optional parameter to control sending MCB message
        )
        {
            _sendMicBMsg = sendMicBMsg;
            _sendRCBMsg = sendRCBMsg;
            _sendMCBMsg = sendMCBMsg;
            _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
            _agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
            
            
            _UserData = UserData ?? throw new ArgumentNullException(nameof(UserData));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _onCompletedCallback = onCompletedCallback; // Store the callback
            _lifetimeStopwatch.Start();
            //_agentRepository.Dispatcher.RegisterAgentMessageCallback(_agentName, OnEstopRequestReceived);
        
            SendCmd(start_cmd); // Send the initial command to start the estop process

            refreshTimer = new System.Timers.Timer(800);
            refreshTimer.Elapsed += TimePast;
            refreshTimer.Start();
        }

        private void TimePast(object? sender, System.Timers.ElapsedEventArgs e)
        {
            Console.WriteLine($"[EstopRequestManager:{_instanceId}] TimePast called.");
            refreshTimer?.Stop();
            refreshTimer?.Dispose();
            refreshTimer = null;

            SendCmd(eEstopStateCmd.eEstopStateNoChange); // Send a no-change command to reset the estop state

            Dispose(); // Dispose after the reset is done
        }
        public void SendCmd(eEstopStateCmd estop_cmd)
        {
            if (_sendRCBMsg)
            {
                Console.WriteLine($"[ResetErrorManager:{_instanceId}] Sending estop command: {estop_cmd}");
                var rc_periodic_msg = _UserData.rc_periodic_msg;
                rc_periodic_msg.estop_cmd = estop_cmd;
                rc_periodic_msg.subsystem_cmd[(int)eRcSubsystems.eRcSubsystemManipulatorLeft]  = eSysState.eInactive;
                rc_periodic_msg.subsystem_cmd[(int)eRcSubsystems.eRcSubsystemManipulatorRight] = eSysState.eInactive;
                _UserData.rc_periodic_msg = rc_periodic_msg;
                _manager.SendServerForwardCmdGeneric((int)DevicesScreen.RC, 2, 1, ref rc_periodic_msg);
            }
            if (_sendMCBMsg)
            {
                var mc_periodic_msg = _UserData.mc_periodic_msg;
                mc_periodic_msg.estop_cmd = estop_cmd;
                _UserData.mc_periodic_msg = mc_periodic_msg;
                _manager.SendServerForwardCmdGeneric((int)DevicesScreen.MC_FAST, 2, 1, ref mc_periodic_msg);
            }
            if (_sendMicBMsg)
            {
                var micb_periodic_msg = _UserData.micb_periodic_msg;
                micb_periodic_msg.estop_state_cmd = estop_cmd;
                _UserData.micb_periodic_msg = micb_periodic_msg;
                _manager.SendServerForwardCmdGeneric((int)DevicesScreen.MICB, 2, 1, ref micb_periodic_msg);
            }
        }
        private void OnEstopRequestReceived(ServerMsg? msg, string agentName, object? result)
        {
            if (result == null)
                return;

            if (isDisposed)
            {
                Console.WriteLine("[OnEstopRequestReceived] Attempted to update after disposal. Ignoring update.");
                return;
            }

            // Use pattern matching to call OnUpdate<T> for each known struct type
            switch (result)
            {
                case MicB2VC_Status:
                case MocB2VC_Status:
                case RC2RKS_Status:
                case MC2RKS_Status:
                    if (_numOfMsgsReceived++ % 20 == 0)
                    {
                        Console.WriteLine($"[OnEstopRequestReceived:{_instanceId}] OnEstopRequestReceived called. isDisposed={isDisposed}");
                    }
                    if (_numOfMsgsReceived > 300)
                    {
                        Console.WriteLine($"OnEstopRequestReceived. Counter: {_numOfMsgsReceived}");
                        SendCmd(eEstopStateCmd.eEstopStateNoChange); // Send a no-change command to reset the estop state
                        Dispose(); // Dispose after the reset is done
                    }
                    break;
            }
        }

        public void Dispose()
        {
            isDisposed = true;
            _onCompletedCallback?.Invoke(); // Fire the callback
            _onCompletedCallback = null; // Clear the callback to prevent multiple invocations

            //_agentRepository.Dispatcher.UnregisterAgentMessageCallback(_agentName, OnEstopRequestReceived);
            _lifetimeStopwatch.Stop();
            _numOfMsgsReceived = 0;

            refreshTimer?.Dispose();
            refreshTimer = null;
            Console.WriteLine($"[ResetErrorManager:{_instanceId}] Disposed and unregistered callback. Lifetime: {_lifetimeStopwatch.ElapsedMilliseconds} ms");
        }
    }
}
