using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Xml.Linq;

namespace MSGS
{
    // Shared DTO for every job-completed message
    public sealed record ScriptProcessed(string JobId, float currentMsg,
        float totalMsgs, string ResultPath, bool isDone, int num_of_times_to_repeat);


    public class MotionScriptEngine : IDisposable
    {
        private readonly HDF5Writter _hdf5Writter;
        private readonly int _guid;
        private readonly bool _shouldRecord;
        private readonly int _numOfGoToMsgs;

        List<object> _linuxTimesStatusMsg = new();
        List<object> _linuxTimesControlMsg = new();
        List<object> _linuxTimesMetryMsg = new();

        Dictionary<(string FatherName, string DatasetName), List<object>> _values = new();

        bool _isScriptDone = false;
        bool _isMetryValid = false;

        private List<byte[]> _rc_device_msgs;
        private int _last_control_msg_index = -1;

        List<int> _tmp_msgs_counter = new();

        private readonly BlockingCollection<HdfFlushData> _flushQueue = new();
        private readonly Thread _flushThread;
        private bool _flushThreadRunning = true;

        private readonly object _writeToHdfLock = new();

        // Add a delegate and event for ScriptProcessed
        public delegate void ScriptProcessedHandler(ScriptProcessed payload);
        private event ScriptProcessedHandler? fireScriptProcessed;

        // This points to the json file where all the extra variables needs to be saved
        private string _jsonTextUserVariables = string.Empty;
        private IddFlatSchema? iddFlatSchema = null;
        private List<string> _logUserVars = new();

        // Register callback
        public void RegisterScriptProcessedCallback(ScriptProcessedHandler callback)
        {
            fireScriptProcessed += callback;
        }

        // Unregister callback
        public void UnregisterScriptProcessedCallback(ScriptProcessedHandler callback)
        {
            fireScriptProcessed -= callback;
        }

        public MotionScriptEngine(
            string load_file, 
            List<byte[]> rc_msgs,
            int guid, 
            bool should_record, 
            string sub_folder, 
            string agentName,
            int numOfGoToMsgs,
            string jsonTextUserVariables)
        {
            _jsonTextUserVariables = jsonTextUserVariables;
            //iddFlatSchema = new IddFlatSchema(_jsonTextUserVariables); //TODELETE!!!!!!!!!!!!!!!!!!
            _guid = guid;
            _rc_device_msgs = rc_msgs;
            _hdf5Writter = new HDF5Writter(load_file, sub_folder, agentName);
            _shouldRecord = should_record;
            _numOfGoToMsgs = numOfGoToMsgs;

            _flushThread = new Thread(FlushThreadProc) { IsBackground = true };
            _flushThread.Start();
        }

        public byte[]? OnCancel()
        {
            try
            {
                Console.WriteLine("OnCancel called in MotionScriptEngine.");
                byte[]? ret_val = null;
                if (_shouldRecord && !_isScriptDone)
                {
                    if (_last_control_msg_index >= 0 || _last_control_msg_index < _rc_device_msgs.Count)
                    {
                        ret_val = _rc_device_msgs[_last_control_msg_index];
                    }
                    else
                    {
                        ret_val = _rc_device_msgs.Count > 0 ? _rc_device_msgs[_rc_device_msgs.Count - 1] : null;
                    }
                    Console.WriteLine($"Script cancelled. index: {_last_control_msg_index}. Count: {_rc_device_msgs.Count}");
                    Dispose();
                }
                return ret_val;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception caught in OnCancel: {ex}");
                return null;
            }
        }

        public void Dispose()
        {
            if (!_isScriptDone)
            {
                Console.WriteLine("Disposing MotionScriptEngine...");
                _jsonTextUserVariables = string.Empty;
                iddFlatSchema = null;
                _isScriptDone = true;
                if (_shouldRecord)
                {
                    WriteToHDF(true);
                    _flushThreadRunning = false;
                    _flushQueue.CompleteAdding();
                    _flushThread.Join();
                    _hdf5Writter.Dispose();
                }
                _tmp_msgs_counter.Clear();

                var payload = new ScriptProcessed("motion_done",
                    _rc_device_msgs.Count,
                    _rc_device_msgs.Count,
                    _hdf5Writter.OutputFileName, true, 0);

                fireScriptProcessed?.Invoke(payload);

                // Unsubscribe all callbacks
                fireScriptProcessed = null;
                Console.WriteLine("MotionScriptEngine disposed done.");
            }
        }

        internal bool OnExtraVariableSave(object? obj)
        {
            if (_jsonTextUserVariables.Length == 0)
            {
                return false;
            }
            if (iddFlatSchema == null)
            {
                iddFlatSchema = new IddFlatSchema(_jsonTextUserVariables);
            }
            Dictionary<string, object?> allValsFromReflection = new Dictionary<string, object?>();
            string name = string.Empty;
            if (iddFlatSchema.TryGetVariablePaths(obj, out var userSelectedVarsToSave))
            {
                //make sure we have something.
                if (userSelectedVarsToSave.Count ==0)
                {
                    return false;
                }
                switch (obj)
                {
                    case MicB2VC_Status micbStatus:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<MicB2VC_Status>(ref micbStatus);
                        name = "user_micb_status";
                        break;
                    case MocB2VC_Status mocbStatus:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<MocB2VC_Status>(ref mocbStatus);
                        name = "user_mocb_status";
                        break;
                    case RC2RKS_Status rcStatus:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<RC2RKS_Status>(ref rcStatus);
                        name = "user_rc_status";
                        break;
                    case MC2RKS_Status mcStatus:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<MC2RKS_Status>(ref mcStatus);
                        name = "user_mc_status";
                        break;
                    case SMicBMetryMsg micbMetry:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<SMicBMetryMsg>(ref micbMetry);
                        name = "user_micb_metry";
                        break;
                    case SMocBMetryMsg mocbMetry:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<SMocBMetryMsg>(ref mocbMetry);
                        name = "user_mocb_metry";
                        break;
                    case SRcDebugMetry rcDebugMetry:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<SRcDebugMetry>(ref rcDebugMetry);
                        name = "user_rcDebug_metry";
                        break;
                    case SRcControlMetry rcControl_metry:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<SRcControlMetry>(ref rcControl_metry);
                        name = "user_rcControl_metry";
                        break;
                    case SMcFastDiagnostics mc_FastMetry:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<SMcFastDiagnostics>(ref mc_FastMetry);
                        name = "user_mc_FastMetry";
                        break;
                    case SMcDisgnostics mc_SlowMetry:
                        allValsFromReflection = ReflectionHelper.GetNestedPropertyValue<SMcDisgnostics>(ref mc_SlowMetry);
                        name = "user_mc_SlowMetry";
                        break;
                    default:
                        name = $"Uknown struct - {obj?.GetType().Name}";
                        break;
                }
                // userSelectedVarsToSave now has entries like:
                // "header.VersionIdd.VersionMajor", "manipulator_cmd_left.target.poseArr[0]", ...
                foreach (var varToSave in userSelectedVarsToSave)
                {
                    if (allValsFromReflection.TryGetValue(varToSave, out var value) && value is not null)
                    {
                        _values.GetOrCreate((name, varToSave)).Add(value);
                    }
                    else
                    {
                        string log_var = ($"Error in hdf5 save - struct {name} does not contain {varToSave}");
                        if (!_logUserVars.Contains(log_var))
                        {
                            Console.WriteLine(log_var);
                            _logUserVars.Add(log_var);
                        }
                    }                   
                }
                return true;
            }
            return false;
        }

        internal bool OnMetryOperUpdate(int lastMsgIndex, 
            long lastUnixTime, 
            int num_of_times_repeat,
            SRcControlMetry rc_metry_oper_reply,
            int id)
        {
            if (_isMetryValid && !_isScriptDone && _shouldRecord)
                
            {
                _linuxTimesMetryMsg.Add((long)lastUnixTime);
                FillMetryMsg(rc_metry_oper_reply);
                return true;
            }
            return false;
        }

        internal bool OnControlMsgSentToDevice(int lastMsgIndex, 
            long lastUnixTime, 
            int num_of_times_repeat,
            int id)
        {
            if (!IsMsgValid(lastMsgIndex, id))
            {
                return false;
            }
            if (_shouldRecord)
            {
                _last_control_msg_index = lastMsgIndex;

                var controlLine = MSGHelper.ByteArrayToStruct<RKS2RC_Control>(
                    _rc_device_msgs[lastMsgIndex]);

                _linuxTimesControlMsg.Add((long)lastUnixTime);
                _values.GetOrCreate(("Control", "Last_msg_index")).Add(lastMsgIndex);
                _values.GetOrCreate(("Control", "NumOfGoToMsgs")).Add(_numOfGoToMsgs);

                FillControlMsg(controlLine);
            }
            return true;
        }

        public bool OnStatusUpdate(
            int lastMsgIndex,
            long lastUnixTime,
            int num_of_times_repeat,
            RC2RKS_Status rc_periodic_status,
            int id)
        {
            _isMetryValid = false;
            if (id != _guid)
            {
                return false;
            }
            if (lastMsgIndex >= _rc_device_msgs.Count - 1)
            {
                if (num_of_times_repeat <= 0)
                {
                    if (!_isScriptDone)
                    {
                        Dispose();
                    }
                }
                return false;
            }

            if (!IsMsgValid(lastMsgIndex, id))
            {
                return false;
            }

            var payload = new ScriptProcessed("motion_in_progress",
                    lastMsgIndex,
                    _rc_device_msgs.Count,
                    _hdf5Writter.OutputFileName, false, num_of_times_repeat);

            fireScriptProcessed?.Invoke(payload);

            if (_shouldRecord)
            {

                _isMetryValid = true;
                _tmp_msgs_counter.Add(lastMsgIndex);

                _linuxTimesStatusMsg.Add((long)lastUnixTime);

                FillStatusMsg(rc_periodic_status);

                WriteToHDF();
            }
            return true;
        }

        private bool IsMsgValid(int lastMsgIndex, int id)
        {
            Debug.Assert(_rc_device_msgs.Count > 0);
            
            if (lastMsgIndex < 1 || _isScriptDone == true || id != _guid || 
                lastMsgIndex >= _rc_device_msgs.Count)
            {
                return false;
            }
            return true;
        }

        private void WriteToHDF(bool forceFlush = false)
        {
            if (_shouldRecord)
            {
                lock (_writeToHdfLock)
                {
                    if (_linuxTimesStatusMsg.Count >= 1024 || forceFlush)
                    {
                        // Deep copy all data for the queue
                        var flushData = new HdfFlushData(
                            new List<object>(_linuxTimesStatusMsg),
                            new List<object>(_linuxTimesControlMsg),
                            new List<object>(_linuxTimesMetryMsg),
                            _values.ToDictionary(
                                kvp => kvp.Key,
                                kvp => new List<object>(kvp.Value)
                            ),
                            new List<int>(_tmp_msgs_counter)
                        );
                        _flushQueue.Add(flushData);

                        // Clear the lists after queueing
                        _linuxTimesStatusMsg.Clear();
                        _linuxTimesControlMsg.Clear();
                        _linuxTimesMetryMsg.Clear();
                        _values.Clear();
                        _tmp_msgs_counter.Clear();
                    }
                }
                // Optionally, you can do logging or other non-critical work outside the lock
            }
        }

        private void FlushThreadProc()
        {
            while (_flushThreadRunning || !_flushQueue.IsCompleted)
            {
                try
                {
                    if (_flushQueue.TryTake(out var flushData, Timeout.Infinite))
                    {
                        _hdf5Writter.FlushToFile(
                            flushData.LinuxTimesStatusMsg,
                            flushData.LinuxTimesControlMsg,
                            flushData.LinuxTimesMetryMsg,
                            flushData.Values,
                            flushData.TmpMsgsCounter
                        );
                    }
                }
                catch (ObjectDisposedException)
                {
                    Console.WriteLine("FlushThreadProc exception Flush thread was disposed, exiting gracefully.");
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"FlushThreadProc exception: {ex}");
                }
            }
        }

        private void FillStatusMsg(RC2RKS_Status status)
        {
            FillMappedFields(status, GenerateStatusMappings, "Status");
        }

        private void FillControlMsg(RKS2RC_Control controlLine)
        {
            FillMappedFields(controlLine, GenerateControlMappings, "Control");
        }

        private void FillMetryMsg(SRcControlMetry controlMetry)
        {
            FillMappedFields(controlMetry, GenerateMetryMappings, "Metry");
        }

        private void FillMappedFields<T>(
            T source,
            Func<T, (string name, object value)[]> mappingGenerator,
            string fatherName)
        {
            var mappings = mappingGenerator(source);
            foreach (var (name, value) in mappings)
            {
                //hdf.AddValue(name, value);
                _values.GetOrCreate((fatherName, name)).Add(value);
            }
        }

        
        private static (string name, object value)[] GenerateStatusMappings(
            RC2RKS_Status rc_stat)
        {
            sRcManipulatorStatus[] statuses = rc_stat.manipulator_status;
            var list = new List<(string, object)>();

            list.Add(("status_echo_counter", rc_stat.echo_counter));

            for (int side = 0; side < 2; side++)
            {
                var sideLabel = side == 0 ? "left" : "right";
                var status = statuses[side];

                list.Add(($"status_side_{sideLabel}_rc_encoders_time_tag", status.rc_encoders_time_tag));
                                         
                list.Add(($"status_side_{sideLabel}_pose_m1", status.pose.m1));
                list.Add(($"status_side_{sideLabel}_pose_m2", status.pose.m2));
                list.Add(($"status_side_{sideLabel}_pose_m3", status.pose.m3));
                list.Add(($"status_side_{sideLabel}_pose_m4", status.pose.m4));
                list.Add(($"status_side_{sideLabel}_pose_m5", status.pose.m5));
                list.Add(($"status_side_{sideLabel}_pose_m6", status.pose.m6));
                list.Add(($"status_side_{sideLabel}_pose_plunger", status.pose.plunger));
                                         
                list.Add(($"status_side_{sideLabel}_tool_id", status.tool_id));
                list.Add(($"status_side_{sideLabel}_tool_holder", status.tool_holder));
                list.Add(($"status_side_{sideLabel}_drape_lock_status", status.drape_lock_status));
                list.Add(($"status_side_{sideLabel}_plunger_button_status", status.plunger_button_status));
                list.Add(($"status_side_{sideLabel}_pusher_limiters", status.pusher_limiters));
                list.Add(($"status_side_{sideLabel}_pressure_calib_status", status.pressure_calib_status));
                                         
                list.Add(($"status_side_{sideLabel}_imu_time_tag", status.imu_data.imu_time_tag));
                list.Add(($"status_side_{sideLabel}_imu_0", status.imu_data.imu_data[0]));
                list.Add(($"status_side_{sideLabel}_imu_1", status.imu_data.imu_data[1]));
                list.Add(($"status_side_{sideLabel}_imu_2", status.imu_data.imu_data[2]));
                list.Add(($"status_side_{sideLabel}_imu_3", status.imu_data.imu_data[3]));
                list.Add(($"status_side_{sideLabel}_imu_4", status.imu_data.imu_data[4]));
                list.Add(($"status_side_{sideLabel}_imu_5", status.imu_data.imu_data[5]));

                list.Add(($"status_side_{sideLabel}_tool_id", status.tool_id));
            }
            return list.ToArray();
        }

        private static (string name, object value)[] GenerateControlMappings(RKS2RC_Control controlLine)
        {
            var list = new List<(string, object)>();
            for (int side = 0; side < 2; side++)
            {
                var sideLabel = side == 0 ? "left" : "right";

                var manipulator = side == 0
                    ? controlLine.manipulator_cmd_left
                    : controlLine.manipulator_cmd_right;
                
                list.Add(($"cmd_side_{sideLabel}_slow_mode", manipulator.slow_mode));

                list.Add(($"cmd_side_{sideLabel}_algo_type_0", manipulator.algo_type[0]));
                list.Add(($"cmd_side_{sideLabel}_algo_type_1", manipulator.algo_type[1]));
                list.Add(($"cmd_side_{sideLabel}_algo_type_2", manipulator.algo_type[2]));
                list.Add(($"cmd_side_{sideLabel}_algo_type_3", manipulator.algo_type[3]));
                list.Add(($"cmd_side_{sideLabel}_algo_type_4", manipulator.algo_type[4]));
                list.Add(($"cmd_side_{sideLabel}_algo_type_5", manipulator.algo_type[5]));
                list.Add(($"cmd_side_{sideLabel}_algo_type_6", manipulator.algo_type[6]));
                                      
                list.Add(($"cmd_side_{sideLabel}_target_m1", manipulator.target.m1));
                list.Add(($"cmd_side_{sideLabel}_target_m2", manipulator.target.m2));
                list.Add(($"cmd_side_{sideLabel}_target_m3", manipulator.target.m3));
                list.Add(($"cmd_side_{sideLabel}_target_m4", manipulator.target.m4));
                list.Add(($"cmd_side_{sideLabel}_target_m5", manipulator.target.m5));
                list.Add(($"cmd_side_{sideLabel}_target_m6", manipulator.target.m6));
                list.Add(($"cmd_side_{sideLabel}_target_plunger", manipulator.target.plunger));
                                      
                list.Add(($"cmd_side_{sideLabel}_target_vel_m1", manipulator.target_velocity.m1));
                list.Add(($"cmd_side_{sideLabel}_target_vel_m2", manipulator.target_velocity.m2));
                list.Add(($"cmd_side_{sideLabel}_target_vel_m3", manipulator.target_velocity.m3));
                list.Add(($"cmd_side_{sideLabel}_target_vel_m4", manipulator.target_velocity.m4));
                list.Add(($"cmd_side_{sideLabel}_target_vel_m5", manipulator.target_velocity.m5));
                list.Add(($"cmd_side_{sideLabel}_target_vel_m6", manipulator.target_velocity.m6));
                list.Add(($"cmd_side_{sideLabel}_target_vel_plunger", manipulator.target_velocity.plunger));

                list.Add(($"cmd_side_{sideLabel}_tool_id", manipulator.tool_id));
                list.Add(($"cmd_side_{sideLabel}_script_line_num_freq_only", (float)manipulator.spare));

            }
            return list.ToArray();
        }

        private (string name, object value)[] GenerateMetryMappings(
            SRcControlMetry control_metry)
        {
            var list = new List<(string, object)>
            {
                ("metry_time_tag", control_metry.u64timeTag),
                ("counter", control_metry.sHeader.Counter)
            };
            for (int side = 0; side < 2; side++)
            {
                var sideLabel = side == 0 ? "left" : "right";

                var motors = control_metry.asManipulatorMetry[side].asAxisMetryData;

                for (int motor = 0; motor <= 6; motor++)
                {
                    var axis = motors[motor];

                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_enc0_raw", axis.Encoders[0].i32Raw));
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_enc0_phys", axis.Encoders[0].f32Phys));
                                            
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_enc1_raw", axis.Encoders[1].i32Raw));
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_enc1_phys", axis.Encoders[1].f32Phys));
                                            
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_ctrl_algo", axis.u8ControlAlgorithmType));
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_temp", axis.u8Temperature));
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_pwm", axis.i8Pwm));
                                            
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_pid_input", axis.sPIDDebugData.r32Input));
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_pid_output", axis.sPIDDebugData.r32Output));
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_pid_dbg1", axis.sPIDDebugData.r32Dbg1));
                    list.Add(($"metry_side_{sideLabel}_motor_{motor}_pid_pwm", axis.sPIDDebugData.i16Pwm));
                }


                var board = control_metry.asManipulatorMetry[side].asBoardData[2];
                list.Add(($"metry_side_{sideLabel}_board[2]_buttons", board.u8Buttons));
                list.Add(($"metry_side_{sideLabel}_board[2]_hall_0", board.i16RawHallEEB[0]));
                list.Add(($"metry_side_{sideLabel}_board[2]_hall_1", board.i16RawHallEEB[1]));
                list.Add(($"metry_side_{sideLabel}_board[2]_hall_2", board.i16RawHallEEB[2]));
            }
            return list.ToArray();
        }

        private class HdfFlushData
        {
            public List<object> LinuxTimesStatusMsg { get; }
            public List<object> LinuxTimesControlMsg { get; }
            public List<object> LinuxTimesMetryMsg { get; }
            public Dictionary<(string FatherName, string DatasetName), List<object>> Values { get; }
            public List<int> TmpMsgsCounter { get; }

            public HdfFlushData(
                List<object> status,
                List<object> control,
                List<object> metry,
                Dictionary<(string, string), List<object>> values,
                List<int> tmpMsgs)
            {
                LinuxTimesStatusMsg = new List<object>(status);
                LinuxTimesControlMsg = new List<object>(control);
                LinuxTimesMetryMsg = new List<object>(metry);
                Values = values.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new List<object>(kvp.Value)
                );
                TmpMsgsCounter = new List<int>(tmpMsgs);
            }
        }
             
    }

    public static class DictionaryExtensions
    {
        public static TValue GetOrCreate<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key)
            where TKey : notnull
            where TValue : new()
        {
            if (!dict.TryGetValue(key, out var value))
            {
                value = new TValue();
                dict[key!] = value;
            }
            return value;
        }
    }
}
