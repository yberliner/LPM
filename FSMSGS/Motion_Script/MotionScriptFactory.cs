using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace MSGS
{
    public class IddEntry
    {
        public required string Name { get; set; }
        public required string IDDName { get; set; }
    }

    public class MotionScriptFactory
    {
        private List<IddEntry>? _iddEntries = null;
        
        private readonly Dictionary<int, string> _deviceIDDVariables = new();
        private readonly string _scriptFileName;

        Dictionary<string, List<IddEntry>>? _dict;
        List<IddEntry>? _jsonGlobalEntries = null;
        bool _device_id_col_exists = false;  
        public bool _is_freq_script = false;
        private readonly int _num_of_times_to_repeat;

        public int NumOfGoToMsgs = 0;

        public MotionScriptFactory(string scriptFileName, int num_of_times_to_repeat)
        {
            _scriptFileName = scriptFileName;

            BuildEntriesFromJson();
            _num_of_times_to_repeat = num_of_times_to_repeat;
        }

        private void BuildEntriesFromJson()
        {
            string jsonPath = Path.Combine(AppContext.BaseDirectory, "Motion_Script", 
                "IDD_alias_map.json");

            
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException($"IDD_alias_map.json not found at: {jsonPath}");

            string json = File.ReadAllText(jsonPath);
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            _dict = JsonSerializer.Deserialize<Dictionary<string, List<IddEntry>>>(json, options);

            _jsonGlobalEntries = _dict?.GetValueOrDefault("Global") ?? [];
        }

        public byte[]? CreateFromFile<T>(int device_id, int deviceTypeMsg, 
            ref DeviceForwardData forwardData, List<byte[]> device_msgs, 
            T periodic_msg, ClientSessionData UserData) where T : struct
        {
            string device_name = typeof(T).Name;
            if (!_device_id_col_exists && device_id != (int)DevicesScreen.RC)
            {
                return null;
            }
            
            var deviceJsonEntries = _dict?.GetValueOrDefault(device_name) ?? [];
            Debug.Assert(deviceJsonEntries.Count > 0);

            _iddEntries = _jsonGlobalEntries!          // global goes first
              .Concat(deviceJsonEntries)  // then the RKS2RC_Control list
              .ToList();

            if (!GenerateVariablesFromFirstLine())
            {
                Console.WriteLine("[Error] Could not read the first line of the file.");
                return null;
            }

            CreateMsgsForDevice(device_msgs, periodic_msg, device_id, UserData);
            if (device_msgs.Count > 0)
            {
                byte[] tcpMsgBytes = MessageCreator.CreateForwardMessage(device_id, deviceTypeMsg,
                ref device_msgs, out forwardData, Cmd.Forward, _num_of_times_to_repeat);

                return tcpMsgBytes;
            }

            return null;

        }

        private bool GenerateVariablesFromFirstLine()
        {
            _deviceIDDVariables.Clear();

            var firstLine = File.ReadLines(_scriptFileName).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine))
                return false;

            var headers = firstLine.Split(',').Select(h => h.Trim()).ToList();
            _device_id_col_exists = headers.Any(h => string.Equals(h, "device_id", StringComparison.OrdinalIgnoreCase));

            for (int i = 0; i < headers.Count; i++)
            {
                string header = headers[i];
                if (string.IsNullOrWhiteSpace(header))
                {
                    Console.WriteLine($"[Warning] Empty header found at column {i}");
                    continue;
                }

                var entry = _iddEntries!.FirstOrDefault(e => e.IDDName == header)
                         ?? _iddEntries!.FirstOrDefault(e => e.Name == header);

                if (entry != null && !string.IsNullOrEmpty(entry.IDDName))
                {
                    _deviceIDDVariables[i] = entry.IDDName;
                }
                else
                {
                    Console.WriteLine($"[Warning] Header not found in IDD map: '{header}' (Column {i})");
                }
            }

            var required = new[] { "MotorID", "AMP", "Freq", "Cycles", "Open" };
            _is_freq_script = required.All(r => _deviceIDDVariables.Values.Contains(r));

            Console.WriteLine($"Generating scripts: Freq_script:{_is_freq_script}.");

            //make sure we have at least one varible to work with - otherwise it is a junk file.
            return _deviceIDDVariables.Count>0;
        }

        public bool CreateMsgsForDevice<T>(List<byte[]> msgs, 
            T periodic_msg_orig, int device_id, ClientSessionData UserData) where T : struct
        {
            

            // Make a deep copy of the struct
            var buffer_orig_msg = MSGHelper.StructureToByteArray(periodic_msg_orig);
            T periodic_msg_copy = MSGHelper.ByteArrayToStruct<T>(buffer_orig_msg);

            bool bLineAdded = false;
            var lines = File.ReadAllLines(_scriptFileName).Skip(1).ToArray(); // skip header line
            int line_num = 1;
            for (int q = 0; q < lines.Length; q++)
            {
                var line = lines[q];
                bool isLastLine = q == lines.Length - 1;

                if (_is_freq_script)
                {
                    // for frequency every line is a new test.
                    periodic_msg_copy = MSGHelper.ByteArrayToStruct<T>(buffer_orig_msg);

                    bLineAdded = true;
                    AddFreqLine(msgs, periodic_msg_copy, line, line_num++, isLastLine);

                    //pause for 100 ms
                    for (int i = 0; i < 48; i++) 
                    {
                        byte[] next_msg = MSGHelper.StructureToByteArray(periodic_msg_orig);
                        msgs.Add(next_msg);
                    }
                }
                else
                {
                    T copy = AddMotionScriptLine(
                        msgs, 
                        device_id, 
                        periodic_msg_copy, 
                        bLineAdded, line);

                    //First - check it it an RC and apply GotoEngine if it is.
                    if (line_num == 1 && device_id == (int)DevicesScreen.RC && (typeof(T) == typeof(RKS2RC_Control)))
                    {
                        //var rcCmd = (RKS2RC_Control)(object)periodic_msg_orig;
                        var rcStatus = UserData.rc_periodic_status;
                        RKS2RC_Control rcCopy = (RKS2RC_Control)(object)copy;

                        for (int i = 0; i < (int)(int)eRcSubsystems.eRcNumOfSubsystems; i++)
                        {
                            rcCopy.subsystem_cmd[i] = eSysState.eActive; //eSysState.eInactive;
                            rcCopy.reset_errors[i] = eModuleErrorState.eModuleErrorStateClear; //for inetgration
                        }

                        //when doing goto use fast mode.
                        rcCopy.manipulator_cmd_left.slow_mode  = eRcOperationMode.eRcOperationModeFast;
                        rcCopy.manipulator_cmd_right.slow_mode = eRcOperationMode.eRcOperationModeFast;

                        List<byte[]> goto_msgs = new GoToEngine().Generate(
                            ref rcCopy, rcStatus, true, 35);
                        msgs.InsertRange(0, goto_msgs);

                        //store the number of GoTo messages
                        NumOfGoToMsgs = goto_msgs.Count;
                    }

                    line_num++;

                }
            }
            return bLineAdded;
        }

        private void AddFreqLine<T>(List<byte[]> msgs, 
            T periodic_msg_copy, string line, int line_num, bool isLastLine) where T : struct
        {
            T copy = periodic_msg_copy;
            List<string> fields = line!.Split(',').Select(f => f.Trim()).ToList();
            int motor_id    = Convert.ToInt32(fields[0]);
            int amp         = Convert.ToInt32(fields[1]);
            double Freq     = Convert.ToDouble(fields[2]);
            double Cycles   = Convert.ToDouble(fields[3]);
            byte Open       = Convert.ToByte(fields[4]);
            byte arms       = fields.Count > 5 ? 
                              Convert.ToByte(fields[5]) : (byte)0; // default to 0 if not specified
            
            motor_id -= 1;
            List<string> algoes = new List<string>(); //store the algoes for last line

            for (int side = 0; side < 2; side++)
            {
                if (arms == 1 && side == 1)
                {
                    continue; // skip right arm if arms == 1
                }
                else if(arms == 2 && side == 0)
                {
                    continue; // skip left arm if arms == 2
                }
                    
                var sideLabel = side == 0 ? "left" : "right";

                string motorBasestr = $"manipulator_cmd_{sideLabel}.";
                string motorAlgo = $"{motorBasestr}algo_type[{motor_id}]";
                string motorAmp = $"{motorBasestr}target.poseArr[{motor_id}]";
                string motorTool = $"{motorBasestr}tool_id";
                string script_line_num = $"{motorBasestr}spare";
                algoes.Add(motorAlgo); //for last line

                eRcAlgoType algoType = Open > 0 ?
                    eRcAlgoType.eRcAlgoTypeInjection1 : eRcAlgoType.eRcAlgoTypeInjection2;

                ReflectionHelper.SetNestedPropertyValue(ref copy, motorAlgo, algoType);

                ReflectionHelper.SetNestedPropertyValue(ref copy, motorAmp, amp);

                ReflectionHelper.SetNestedPropertyValue(ref copy, motorTool, Freq);
                ReflectionHelper.SetNestedPropertyValue(ref copy, script_line_num, (byte)line_num);  
            }
            double num_of_times = (Cycles / Freq) * (double)480.0;

            for (int i = 0; i < num_of_times; i++)
            {
                byte[] next_msg = MSGHelper.StructureToByteArray(copy);
                msgs.Add(next_msg);
            }

            // In case of last line move back to normal algo type
            if (isLastLine)
            {
                foreach (var algo in algoes)
                {
                    ReflectionHelper.SetNestedPropertyValue(ref copy, algo, eRcAlgoType.eRcAlgoTypePositionLoop);
                }
                byte[] next_msg = MSGHelper.StructureToByteArray(copy);
                msgs.Add(next_msg);
            }
        }

        private T AddMotionScriptLine<T>(
            List<byte[]> msgs, 
            int device_id, 
            T periodic_msg_copy, 
            bool bLineAdded, 
            string? line) where T : struct
        {
            T copy = periodic_msg_copy;
            var fields = line!.Split(',').Select(f => f.Trim()).ToList();
            int num_of_times_to_repeat = 1; //default is one per line
            int to_device_line = (int)DevicesScreen.RC; //RC is the default.

            //check if it is the correct device.
            if (_deviceIDDVariables.ContainsValue("device_id"))
            {
                // Get the first key where the value is "device_id"
                int line_deviceId_field = _deviceIDDVariables
                                .First(kv => kv.Value == "device_id").Key;
                int line_id = Convert.ToInt32(fields[line_deviceId_field]);
                
                if (line_id != device_id)
                {
                    return copy;
                }
            }

            foreach (var kvp in _deviceIDDVariables)
            {
                int columnIndex = kvp.Key;
                string iddName = kvp.Value;
                string varValue = fields[columnIndex];
                if (float.TryParse(varValue, out var value))
                {
                    if (iddName == "num_of_times_to_repeat")
                    {
                        num_of_times_to_repeat = (int)value;
                    }
                    else if (iddName == "device_id")
                    {
                        to_device_line = (int)value;
                    }
                    else
                    {
                        try
                        {
                            ReflectionHelper.SetNestedPropertyValue(ref copy, iddName, value);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Exception SetNestedPropertyValue. {e.ToString()}");
                        }
                        
                    }
                }
                else
                {
                    Console.WriteLine($"Error parsing line. Index: {columnIndex}. name: {iddName}. Value:{varValue}");
                }
            }

            const int MaxDurationSeconds = 600;
            const int SamplingRateHz = 480;
            const int MaxRepeats = SamplingRateHz * MaxDurationSeconds;

            if (num_of_times_to_repeat > MaxRepeats)
            {
                num_of_times_to_repeat = MaxRepeats;
            }


            if (to_device_line == device_id)
            {
                bLineAdded = true;
                for (int i = 0; i < num_of_times_to_repeat; i++)
                {
                    byte[] next_msg = MSGHelper.StructureToByteArray(copy);
                    msgs.Add(next_msg);
                }
            }

            return copy;
        }
    }
}
