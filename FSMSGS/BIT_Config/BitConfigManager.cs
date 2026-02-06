using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class BitConfigManager
    {
        private readonly ClientSessionData _session;
        private readonly OutgoingMsgsManager _outMsgsManager;
        private readonly string agentName;
        private System.Threading.ManualResetEventSlim? _bitStatusEvent;
        List<sBitConfig> _bitsFromDevice = new List<sBitConfig>();
        sBitConfig last_received_bit = new sBitConfig();
        public int num_of_answers = 0;

        public BitConfigManager(
            OutgoingMsgsManager outMsgsManager, 
            string agentName, 
            ClientSessionData session)
        {
            _outMsgsManager = outMsgsManager ?? throw new ArgumentNullException(nameof(outMsgsManager));
            this.agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        private DevicesScreen GetDevice(eSubSystemId_Service_SW_Only subsystem_type)
        {
            switch (subsystem_type)
            {
                case eSubSystemId_Service_SW_Only.eSubSystemIdRws:
                    return DevicesScreen.RC_CONFIG; // Microscope Base
                case eSubSystemId_Service_SW_Only.eSubSystemIdSws:
                    return DevicesScreen.MC_FAST_CONFIG; // Microscope Base
                case eSubSystemId_Service_SW_Only.eSubSystemIdMws:
                    return DevicesScreen.MOCB_CONFIG; // Microscope Base
                case eSubSystemId_Service_SW_Only.eSubSystemIdMicB:
                    return DevicesScreen.MICB_CONFIG; // Microscope Base
                default:
                    Console.WriteLine("Invalid subsystem type. Cannot determine device screen.");
                    throw new ArgumentException("Invalid subsystem type", nameof(subsystem_type));
            }
        }

        private bool SendBitConfigWithRetry(
            ref sBitConfigControl bitControl, 
            sBitConfig bit, int maxRetries,
            DevicesScreen config_device)
        {
            int attempt = 0;
            bool retValue = false;
            while (true)
            {
                attempt++;
                //Console.WriteLine($"Sending BIT config (attempt {attempt}): Error ID = {bit.error_id}, Subsystem ID = {bit.subsystem_id}, Module ID = {bit.module_id}, Unit ID = {bit.unit_id}");
                _outMsgsManager.SendServerCmdGeneric((int)config_device, 3, 1, ref bitControl, agentName, Cmd.OneTimeForward);

                var sw = Stopwatch.StartNew();
                bool signaled = _bitStatusEvent!.Wait(1500);
                sw.Stop();
                if (signaled)
                {
                    Console.WriteLine($"BIT config status received. Waited {sw.ElapsedMilliseconds}");
                    retValue = true;
                    _bitStatusEvent.Reset();
                    break;
                }
                else
                {
                    //Console.WriteLine("Timeout waiting for BIT config status. Resending...");
                    _bitStatusEvent.Reset();
                    if (attempt >= maxRetries)
                    {
                        Console.WriteLine("Max retries reached. Giving up on this BIT config.");
                        break;
                    }
                }
            }
            return retValue;
        }

        private void bitStatusCallback(sBitConfigStatus status)
        {
            if (num_of_answers++ % 10 == 0)
            {
                Console.WriteLine($"BIT Status Callback: Error ID = {status.bit_config.error_id}, " +
                    $"Subsystem ID = {status.bit_config.subsystem_id}, " +
                    $"Module ID = {status.bit_config.module_id}, " +
                    $"Unit ID = {status.bit_config.unit_id}," +
                    $"Subtest ID = {status.bit_config.subtest_id}," +
                    $" set = {status.set_ack} " +
                    $"echo_counter = {status.echo_counter}" +
                    $"num_of_answers = {num_of_answers}");
            }
            last_received_bit = status.bit_config;
            _bitsFromDevice.Add(status.bit_config);

            _bitStatusEvent?.Set();
        }

        public void Init()
        {
            // This method is a placeholder for the actual implementation
            // that initializes the BitConfigManager.
            Console.WriteLine("Initializing BitConfigManager...");
            _bitsFromDevice.Clear();
            num_of_answers = 0;
            _bitStatusEvent = new System.Threading.ManualResetEventSlim(false);
            //_agentsRepository.Dispatcher.RegisterBitConfigStatusCallback(bitStatusCallback);
            _session.RegisterBitConfigCallBack(bitStatusCallback);
        }
        public void Dispose()
        {
            // This method is a placeholder for the actual implementation
            // that disposes of the BitConfigManager resources.
            Console.WriteLine("Disposing BitConfigManager...");
            
            _session.UnregisterBitConfigStatusCallback(bitStatusCallback);
            _bitStatusEvent?.Dispose();
            _bitStatusEvent = null;
            _bitsFromDevice.Clear();
        }
        public (bool, IniRule) SendToAgentBits(
            IniRule rule, 
            byte set_bit,
            string RuleHeader)
        {
            try
            {
                sBitConfig bit = RuleIdToBitConfig(rule);
                sBitConfigControl bitControl = new sBitConfigControl();
                bitControl.set_only = set_bit;
                bitControl.bit_config = bit;

                eSubSystemId_Service_SW_Only subsystemId = GetSubSystemID(RuleHeader);

                DevicesScreen device = GetDevice(subsystemId);

                bool success = SendBitConfigWithRetry(ref bitControl, bit, 2, device);
                IniRule reply_rule = BitConfigToRuleId(last_received_bit);
                if (!success)
                {
                    reply_rule = rule;
                }

                reply_rule.RuleID = rule.RuleID; // Preserve the original RuleID
                return (success, reply_rule);
            }
            catch(Exception e)
            {
                Console.WriteLine($"Excpetion in SendToAgentBits. Exception: {e.ToString()}");
                return (false, rule);
            }
        }

        private static sBitConfig RuleIdToBitConfig(IniRule rule)
        {
            return new sBitConfig
            {
                error_id = (UInt16)rule.ErrorID,
                subsystem_id = (eSubSystemId)rule.SubSystemID,
                module_id = (byte)rule.ModuleID,
                unit_id = (byte)rule.UnitID,
                subtest_id = (byte)rule.SubTestID,
                severity = (eBitSeverity)rule.Severity,
                active = (byte)rule.Active,
                param_type_1 = rule.Params[0].ParamType,
                param_type_2 = rule.Params[1].ParamType,
                param_type_3 = rule.Params[2].ParamType,
                param_type_4 = rule.Params[3].ParamType,
                param_1 = (float)rule.Params[0].Param,
                param_2 = (float)rule.Params[1].Param,
                param_3 = (float)rule.Params[2].Param,
                param_4 = (float)rule.Params[3].Param,
                window_size = (UInt32)rule.WindowSize,
                num_of_errors = (UInt32)rule.NumOfErrors
            };
        }

        private static IniRule BitConfigToRuleId(sBitConfig bit)
        {
            return new IniRule
            {
                RuleID = 0, // Set as needed; sBitConfig does not contain RuleID
                SubSystemID = (int)bit.subsystem_id,
                SubTestID = bit.subtest_id,
                UnitID = bit.unit_id,
                ModuleID = bit.module_id,
                ErrorID = bit.error_id,
                Active = bit.active,
                NumOfErrors = (int)bit.num_of_errors,
                Severity = (int)bit.severity,
                WindowSize = (int)bit.window_size,
                Params = new IniRuleParam[]
                {
                    new IniRuleParam { Param = bit.param_1, ParamType = bit.param_type_1 },
                    new IniRuleParam { Param = bit.param_2, ParamType = bit.param_type_2 },
                    new IniRuleParam { Param = bit.param_3, ParamType = bit.param_type_3 },
                    new IniRuleParam { Param = bit.param_4, ParamType = bit.param_type_4 }
                }
            };
        }

        private static eSubSystemId_Service_SW_Only GetSubSystemID(string RuleHeader)
        {
            eSubSystemId_Service_SW_Only subsystemId = eSubSystemId_Service_SW_Only.eSubSystemIdInvalid;

            switch (RuleHeader)
            {
                case "[MicroscopeWsMicbConfigInfo]":
                    subsystemId = eSubSystemId_Service_SW_Only.eSubSystemIdMicB;
                    break;
                case "[MicroscopeWsMocbConfigInfo]":
                    subsystemId = eSubSystemId_Service_SW_Only.eSubSystemIdMws;
                    break;
                case "[RobotWsConfigInfo]":
                    subsystemId = eSubSystemId_Service_SW_Only.eSubSystemIdRws;
                    break;
                case "[SurgeonWsConfigInfo]":
                    subsystemId = eSubSystemId_Service_SW_Only.eSubSystemIdSws;
                    break;
                default:
                    Console.WriteLine($"GetSubSystemID error. Rule header is: {RuleHeader}");
                    subsystemId = eSubSystemId_Service_SW_Only.eSubSystemIdInvalid;
                    break;
            }

            return subsystemId;
        }
    }
}
