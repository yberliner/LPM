using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace MSGS
{
    public class MessageDispatch
    {
        public int tmp_count = 1;
        public static float goodPackets = 1;
        public static float badPackets = 0;
        private int _updateMotionScriptCounterForLog = 0;
        
        private readonly Dictionary<(DevicesScreen device, E_OPCODES opcode),
            Func<byte[], agentData, object?>> _handlers;

        private long _timestamp_us = 0;
        private int _msg_sequence = -1;
        private int _num_of_times_repeat = -1;
        private int _forard_id = -1;

        private bool _errorExistsInMsg = false;
        private eEstopSysStatus estop_sys_status_last = eEstopSysStatus.eEstopHwSysOperational;
        private eSystemEstopReadyness eEstopReady_last = eSystemEstopReadyness.eReady;

        private LogTimer _logTimer = new LogTimer(10);
        Stopwatch sw = new Stopwatch();

        public MessageDispatch()
        {
            _handlers = CreateHandlers();
        }

        private void CheckErrorStates(eModuleState[] errorStates)
        {
            for (int i = 0; i < errorStates.Length; i++)
            {
                if (errorStates[i] != eModuleState.eModuleOk)
                {
                    _errorExistsInMsg = true;
                }
            }
        }

        private Dictionary<(DevicesScreen, E_OPCODES), int> _numOfMsgs = new();

        private Dictionary<(DevicesScreen, E_OPCODES), Func<byte[], agentData, object?>> CreateHandlers()
        {
            return new Dictionary<(DevicesScreen, E_OPCODES), Func<byte[], agentData, object?>>
            {
                { (DevicesScreen.MICB, E_OPCODES.OP_CONTROLLER_PERIODIC_STATUS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.MicbPeriodicStatus, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<MicB2VC_Status>(buf);
                        data.micb_periodic_status = status;
                        estop_sys_status_last = status.estop_sys_status;
                        eEstopReady_last = status.eEstopReady;

                        //_estopStateCmd = status.estop_state_cmd;
                        CheckErrorStates(status.error_state);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MICB, E_OPCODES.OP_CONTROLLER_INIT_STATUS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.MicbInitReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<MicB2VC_Init>(buf);
                        data.micb_init_reply = status;
                        CheckErrorStates(status.error_state);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MICB_METRY, E_OPCODES.OP_CONTROLLER_METRY_EXTRAS), (buf, data) => {
                    //TODO: UNCOMMENT ONCE MISSING STRUCTS ARE FILLED
                    var status = MSGHelper.ByteArrayToStruct<SMicBMetryMsg>(buf);
                    data.micb_metry_reply = status;
                    return status;
                }},
                { (DevicesScreen.MOCB, E_OPCODES.OP_CONTROLLER_PERIODIC_STATUS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.MocbPeriodicStatus, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<MocB2VC_Status>(buf);
                        data.mocb_periodic_status = status;
                        CheckErrorStates(status.error_state);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MOCB, E_OPCODES.OP_CONTROLLER_INIT_STATUS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.MocbInitReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<MocB2VC_Init>(buf);
                        data.mocb_init_reply = status;
                        CheckErrorStates(status.error_state);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MOCB_METRY, E_OPCODES.OP_CONTROLLER_METRY_EXTRAS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.MocbMetryReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<SMocBMetryMsg>(buf);
                        data.mocb_metry_reply = status;
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.RC, E_OPCODES.OP_CONTROLLER_PERIODIC_STATUS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.RcPeriodicStatus, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<RC2RKS_Status>(buf);
                        CheckErrorStates(status.error_state);

                        data.rc_periodic_status = status;
                        data.motion_engine?.OnStatusUpdate(
                            _msg_sequence,
                            _timestamp_us,
                            _num_of_times_repeat,
                            data.rc_periodic_status,
                            _forard_id);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.RC, E_OPCODES.OP_CONTROLLER_INIT_STATUS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.RcInitReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<RC2RKS_Init>(buf);
                        CheckErrorStates(status.error_state);

                        data.rc_init_reply = status;
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.RC_METRY, E_OPCODES.OP_CONTROLLER_METRY_OPER), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.RcMetryOperReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<SRcControlMetry>(buf);
                        data.rc_metry_oper_reply = status;
                        data.motion_engine?.OnMetryOperUpdate(
                            _msg_sequence,
                            _timestamp_us,
                            _num_of_times_repeat,
                            data.rc_metry_oper_reply,
                            _forard_id);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.RC_METRY, E_OPCODES.OP_CONTROLLER_METRY_INIT), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.RcMetryInitReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<SRcDebugMetry>(buf);
                        data.rc_metry_init_reply = status;
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MC_FAST, E_OPCODES.OP_CONTROLLER_PERIODIC_STATUS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.McPeriodicStatus, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<MC2RKS_Status>(buf);
                        CheckErrorStates(status.error_state);

                        data.mc_periodic_status = status;
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MC_FAST, E_OPCODES.OP_CONTROLLER_INIT_STATUS), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.McInitReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<MC2RKS_Init>(buf);
                        CheckErrorStates(status.error_state);

                        data.mc_init_reply = status;
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MC_FAST_METRY, E_OPCODES.OP_CONTROLLER_METRY_OPER), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.McMetryFastReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<SMcFastDiagnostics>(buf);
                        data.mc_metry_fast_reply = status;
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MC_SLOW_METRY, E_OPCODES.OP_CONTROLLER_METRY_OPER), (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.McMetrySlowReply, ref badPackets, ref goodPackets))
                    {
                        var status = MSGHelper.ByteArrayToStruct<SMcDisgnostics>(buf);
                        data.mc_metry_slow_reply = status;
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MICB_METRY, E_OPCODES.OP_CONTROLLER_METRY_INIT),      (buf, data) => null },
                { (DevicesScreen.MICB_METRY, E_OPCODES.OP_CONTROLLER_METRY_OPER),      (buf, data) => null },
                { (DevicesScreen.MOCB_METRY, E_OPCODES.OP_CONTROLLER_METRY_INIT),      (buf, data) => null },
                { (DevicesScreen.MOCB_METRY, E_OPCODES.OP_CONTROLLER_METRY_OPER),      (buf, data) => null },
                { (DevicesScreen.MC_FAST_METRY, E_OPCODES.OP_CONTROLLER_METRY_INIT),   (buf, data) => null },
                { (DevicesScreen.MC_FAST_METRY, E_OPCODES.OP_CONTROLLER_METRY_EXTRAS), (buf, data) => null },
                { (DevicesScreen.RC_METRY, E_OPCODES.OP_CONTROLLER_METRY_EXTRAS),      (buf, data) => null },
                { (DevicesScreen.RC_CONFIG, E_OPCODES.OP_CONTROLLER_CONFIG_STATUS),    (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.configStatus, ref badPackets, ref goodPackets))
                    {
                        Console.WriteLine($"RC config status received: {data.AgentName}");
                        var status = MSGHelper.ByteArrayToStruct<sBitConfigStatus>(buf);
                        
                        data.RaiseBitConfigStatusReceived(status);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MC_FAST_CONFIG, E_OPCODES.OP_CONTROLLER_CONFIG_STATUS),    (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.configStatus, ref badPackets, ref goodPackets))
                    {
                        Console.WriteLine($"MC config status received: {data.AgentName}");
                        var status = MSGHelper.ByteArrayToStruct<sBitConfigStatus>(buf);
                        data.RaiseBitConfigStatusReceived(status);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MOCB_CONFIG, E_OPCODES.OP_CONTROLLER_CONFIG_STATUS),    (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.configStatus, ref badPackets, ref goodPackets))
                    {
                        Console.WriteLine($"MOCB config status received: {data.AgentName}");
                        var status = MSGHelper.ByteArrayToStruct<sBitConfigStatus>(buf);
                        data.RaiseBitConfigStatusReceived(status);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.MICB_CONFIG, E_OPCODES.OP_CONTROLLER_CONFIG_STATUS),    (buf, data) => {
                    if (BufferValidator.IsZeroedFromIndex(buf, StructSizes.configStatus, ref badPackets, ref goodPackets))
                    {
                        Console.WriteLine($"MICB config status received agent: {data.AgentName}");
                        var status = MSGHelper.ByteArrayToStruct<sBitConfigStatus>(buf);
                        data.RaiseBitConfigStatusReceived(status);
                        return status;
                    }
                    return null;
                }},
                { (DevicesScreen.RC_CONFIG, E_OPCODES.OP_MASTER_CONFIG_COMMAND),    (buf, data) => {
                    
                    Console.WriteLine($"RC config Cmd received:");
                    var status = MSGHelper.ByteArrayToStruct<sBitConfigControl>(buf);
                        
                    return status;
                }},
                { (DevicesScreen.MC_FAST_CONFIG, E_OPCODES.OP_MASTER_CONFIG_COMMAND),    (buf, data) => {

                    Console.WriteLine($"MC_FAST config Cmd received:");
                    var status = MSGHelper.ByteArrayToStruct<sBitConfigControl>(buf);

                    return status;
                }},
                { (DevicesScreen.MOCB_CONFIG, E_OPCODES.OP_MASTER_CONFIG_COMMAND),    (buf, data) => {

                    Console.WriteLine($"MOCB config Cmd received:");
                    var status = MSGHelper.ByteArrayToStruct<sBitConfigControl>(buf);

                    return status;
                }},
                { (DevicesScreen.MICB_CONFIG, E_OPCODES.OP_MASTER_CONFIG_COMMAND),    (buf, data) => {

                    Console.WriteLine($"MICB config Cmd received:");
                    var status = MSGHelper.ByteArrayToStruct<sBitConfigControl>(buf);

                    return status;
                }},
            };
        }

        public void DispatchMessage(ref ServerMsg msg, ref string registeredAgentName,
            agentData data)
        {
            sw.Start();

            _errorExistsInMsg = false;
            _timestamp_us = msg.timestamp_us;
            _msg_sequence = msg.msg_sequence;
            _num_of_times_repeat = msg.buffer_max_len;
            _forard_id = msg.forward_id;

            E_OPCODES opcode = (E_OPCODES)msg.buffer[0];

            //first check if it is an update of msg sent to device by agent.
            //and also it is RC msg.
            if (msg.cmd == Cmd.MessageSentToDevice && msg.device == (int)DevicesScreen.RC)
            {
                if (data.motion_engine != null)
                {
                    if (_updateMotionScriptCounterForLog++ % 1000 == 0)
                    {
                        Console.WriteLine($"Update counter for script in message dispatch: {_updateMotionScriptCounterForLog}");
                    }
                    data.motion_engine?.OnControlMsgSentToDevice(
                                _msg_sequence,
                                _timestamp_us,
                                _num_of_times_repeat,
                                _forard_id);
                }
            }
            else if (_handlers.TryGetValue(((DevicesScreen)msg.device, opcode), out var handler))
            {
                var result = handler(msg.buffer, data);

                data.errorState.updateDevice((int)msg.device, opcode, 
                    _errorExistsInMsg, estop_sys_status_last, eEstopReady_last,
                    msg.num_of_non_scriptes_msgs, msg.num_of_scriptes_msgs, msg.log_status);

                //save the extra parameters of the scripts if it exists
                data.motion_engine?.OnExtraVariableSave(result);

                PrintStatistics(((DevicesScreen)msg.device, opcode)); 
            }
            else
            {
                badPackets++;
            }

            sw.Stop();
        }
        
        private void PrintStatistics((DevicesScreen device, E_OPCODES opcode) key)
        {
            // Try to get the current value for the key
            if (_numOfMsgs.TryGetValue(key, out int currentValue))
            {
                // If the key exists, increment the value
                _numOfMsgs[key] = currentValue + 1;
            }
            else
            {
                // If the key does not exist, set it to 1
                _numOfMsgs[key] = 1;
            }

            //print
            if (_logTimer.TimeToLog())
            {
                // Sort the dictionary by value in descending order and print it
                var sortedNumOfMsgs = _numOfMsgs.OrderByDescending(entry => entry.Value);

                Console.WriteLine($"Printing MessageDispatch | busy time:{sw.ElapsedMilliseconds / 1000} s. Total entries: {_numOfMsgs.Values.Sum()}");
                foreach (var entry in sortedNumOfMsgs)
                {
                    var entry_key = entry.Key;
                    var value = entry.Value;
                    Console.WriteLine($"Key: ({entry_key.Item1}, {entry_key.Item2}) => Value: {value}");
                }
            }
        }
    }
}
