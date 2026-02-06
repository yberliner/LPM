using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class ResetErrorManager : IDisposable
    {
        private readonly ClientManager _manager;
        private readonly AgentsRepository _agentRepository;
        ClientSessionData _UserData;
        private readonly string _agentName;
        private readonly uint _firstCounter;
        private int _numOfMsgsReceived = 0;
        public bool isDisposed { get; private set; } = false;

        private System.Timers.Timer? refreshTimer;

        private readonly Stopwatch _lifetimeStopwatch = new Stopwatch();
        private readonly Guid _instanceId = Guid.NewGuid();

        private Func<Task>? _onCompletedCallback; // Add a field for the callback

        public ResetErrorManager(
            AgentsRepository agentRepository, 
            string agentName, 
            MicB2VC_Status firstStatus,
            ClientSessionData UserData,
            ClientManager manager,
            Func<Task>? onCompletedCallback = null // Accept callback in constructor
        )
        {
            _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
            _agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
            
            
            _UserData = UserData ?? throw new ArgumentNullException(nameof(UserData));
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _firstCounter = firstStatus.header.Counter;

            _onCompletedCallback = onCompletedCallback; // Store the callback

            _lifetimeStopwatch.Start();

            SendMicBMsg(eModuleErrorState.eModuleErrorStateClear); 
            SendMocBMsg(eModuleErrorState.eModuleErrorStateClear);
            SendRCBMsg(eModuleErrorState.eModuleErrorStateClear);
            SendMCBMsg(eModuleErrorState.eModuleErrorStateClear);

            refreshTimer = new System.Timers.Timer(800);
            refreshTimer.Elapsed += TimePast;
            refreshTimer.Start();

            //_agentRepository.Dispatcher.RegisterAgentMessageCallback(_agentName, OnResetErrorReceived);

        }
        private void TimePast(object? sender, System.Timers.ElapsedEventArgs e)
        {
            Console.WriteLine($"[ResetErrorManager:{_instanceId}] TimePast called.");
            refreshTimer?.Stop();
            refreshTimer?.Dispose();
            refreshTimer = null;

            SendMicBMsg(eModuleErrorState.eModuleErrorStateIgnore);
            SendMocBMsg(eModuleErrorState.eModuleErrorStateIgnore);
            SendRCBMsg(eModuleErrorState.eModuleErrorStateIgnore);
            SendMCBMsg(eModuleErrorState.eModuleErrorStateIgnore);

            Dispose(); // Dispose after the reset is done
        }
        

        private void OnResetErrorReceived(ServerMsg? msg, string agentName, object? result)
        {
            if (result == null)
                return;

            if (isDisposed)
            {
                Console.WriteLine("[ResetError] Attempted to update after disposal. Ignoring update.");
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
                        Console.WriteLine($"[ResetErrorManager:{_instanceId}] OnResetErrorReceived called. isDisposed={isDisposed}");
                    }
                    if (_numOfMsgsReceived >300)
                    {
                        Console.WriteLine($"Reset Error Done. Counter: {_numOfMsgsReceived}, First Counter: {_firstCounter}");
                        SendMicBMsg(eModuleErrorState.eModuleErrorStateIgnore);
                        SendMocBMsg(eModuleErrorState.eModuleErrorStateIgnore);
                        SendRCBMsg(eModuleErrorState.eModuleErrorStateIgnore);
                        SendMCBMsg(eModuleErrorState.eModuleErrorStateIgnore);

                        Dispose(); // Dispose after the reset is done
                    }
                    //OnUpdateMicB(micbStatus);
                    break;
            }
        }

       
        private void SendMicBMsg(eModuleErrorState error_state)
        {
            SendResetMsg<VC2MicB_Control>(
                    DevicesScreen.MICB,
                    _UserData.micb_periodic_msg,
                    (int)eMicbBitModules.eMicbNumOfBitModules,
                    error_state);
        }

        private void SendMocBMsg(eModuleErrorState error_state)
        {
            SendResetMsg<VC2MocB_Control>(
                            DevicesScreen.MOCB,
                            _UserData.mocb_periodic_msg,
                            (int)e_mocb_subsystem_modules.E_MOCB_NUM_BIT_MODULES,
                            error_state);
        }

        private void SendRCBMsg(eModuleErrorState error_state)
        {
            SendResetMsg<RKS2RC_Control>(
                            DevicesScreen.RC,
                            _UserData.rc_periodic_msg,
                            (int)eRcSubsystems.eRcNumOfSubsystems,
                            error_state);
        }

        private void SendMCBMsg(eModuleErrorState error_state)
        {
            SendResetMsg<RKS2MC_Control>(
                            DevicesScreen.MC_FAST,
                            _UserData.mc_periodic_msg,
                            (int)McBitModules.NumOfBitModules,
                            error_state);
        }

        private void SendResetMsg<TControl>(
                    DevicesScreen device,
                    TControl periodicMsg,
                    int resetErrorsLength,
                    eModuleErrorState errorState
                ) where TControl : struct
        {
            byte[] bytes = MSGHelper.StructureToByteArray<TControl>(periodicMsg);
            TControl control = MSGHelper.ByteArrayToStruct<TControl>(bytes);

            for (int i = 0; i < resetErrorsLength; i++)
            {
                string reset_error = $"reset_errors[{i}]";
                ReflectionHelper.SetNestedPropertyValue(ref control, reset_error, errorState);
            }

            _manager.SendServerForwardCmdGeneric((int)device, 2, 1, ref control);
        }

        public void Dispose()
        {
            isDisposed = true;
            _onCompletedCallback?.Invoke(); // Fire the callback
            _onCompletedCallback = null; // Clear the callback to prevent multiple invocations

            //_agentRepository.Dispatcher.UnregisterAgentMessageCallback(_agentName, OnResetErrorReceived);
            _lifetimeStopwatch.Stop();
            _numOfMsgsReceived = 0;

            refreshTimer?.Dispose();
            Console.WriteLine($"[ResetErrorManager:{_instanceId}] Disposed and unregistered callback. Lifetime: {_lifetimeStopwatch.ElapsedMilliseconds} ms");
        }
    }
}
