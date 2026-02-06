using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class GeneralSaver : IDisposable
    {
        private readonly AgentsRepository _agentRepository;
        private readonly string _agentName;
        private HDF5Writter? _hdf5Writter;
        private Dictionary<(string FatherName, string DatasetName), List<object>> _values = new();
        private List<object> _dummyList = new List<object>(); 
        private int _numOfUpdates = 0;
        private int _config_status_index = 0;
        private int _config_cmd_index = 0;
        public bool isDisposed { get; private set; } = false;

        public GeneralSaver(string agentName, AgentsRepository agentRpository)
        {
            // Fix for CS8602: Ensure _hdf5Writter is initialized before usage
            _hdf5Writter = new HDF5Writter();
            _hdf5Writter.GenerateGeneralSaverName();
            _agentRepository = agentRpository ?? throw new ArgumentNullException(nameof(agentRpository));

            //Todo: Fix this.
            //_agentRepository.Dispatcher.RegisterAgentMessageCallback(agentName, OnAgentMessageReceived);

            _agentName = agentName ?? throw new ArgumentNullException(nameof(agentName));

        }

        public void Dispose()
        {
            if (_hdf5Writter != null)
            {
                isDisposed = true; // Set the disposed flag to true

                // todo: Fix this.
                //_agentRepository.Dispatcher.UnregisterAgentMessageCallback(_agentName, OnAgentMessageReceived);

                _hdf5Writter.FlushToFile(_dummyList, // Updated to use the renamed field
                                     _dummyList,
                                     _dummyList,
                                     _values,
                                     new List<int>());
                _hdf5Writter.Dispose();
                _values.Clear();
                //_values = null;
                _hdf5Writter = null;
                Console.WriteLine("[GeneralSaver] Disposed and flushed data to file.");
            }
        }
        public void OnUpdate<T>(T value) where T : struct
        {
            if (isDisposed)
            {
                Console.WriteLine("[GeneralSaver] Attempted to update after disposal. Ignoring update.");
                return;
            }
            string FatherName = typeof(T).Name;
            Dictionary<string, object> vals = ReflectionHelper.GetNestedPropertyValue<T>(ref value);
            string? configName = null;
            if (FatherName == "sBitConfigStatus")// || FatherName == "sBitConfigControl")
            {
                configName = FatherName + _config_status_index++.ToString("D4");
            }
            else if (FatherName == "sBitConfigControl")
            {
                configName = FatherName + _config_cmd_index++.ToString("D4");
            }
            foreach (var kvp in vals)
            {
                string datasetName = kvp.Key;
                object datasetValue = kvp.Value;
                AddToDictionary(FatherName, datasetName, datasetValue);

                //add config status and config commmand as an entry
                if (configName != null)
                {
                    AddToDictionary(configName, datasetName, datasetValue);
                }
            }
            _numOfUpdates++;
            //if (_numOfUpdates >= 1024)
            //{
            //    // Flush to file every 1000 updates
            //    _hdf5Writter?.FlushToFile(_dummyList, _dummyList, _dummyList, _values, new List<int>());
            //    _numOfUpdates = 0;
            //}

            if (_numOfUpdates % 5000 == 0)
            {
                Console.WriteLine($"[GeneralSaver] Processed {_numOfUpdates} updates so far.");
            }
            
        }

        private void AddToDictionary(string FatherName, string datasetName, object datasetValue)
        {
            if (!_values.ContainsKey((FatherName, datasetName)))
            {
                _values[(FatherName, datasetName)] = new List<object>();
            }
            // Add the value to the list for this dataset
            _values[(FatherName, datasetName)].Add(datasetValue);
        }

        // The callback function
        public void OnAgentMessageReceived(
            ServerMsg? msg, 
            string agentName, 
            object? result)
        {
            if (result == null)
                return;

            // Use pattern matching to call OnUpdate<T> for each known struct type
            switch (result)
            {
                case MicB2VC_Status micbStatus:
                    OnUpdate(micbStatus);
                    break;
                case MicB2VC_Init micbInit:
                    OnUpdate(micbInit);
                    break;
                case SMicBMetryMsg micbMetry:
                    OnUpdate(micbMetry);
                    break;
                case MocB2VC_Status mocbStatus:
                    OnUpdate(mocbStatus);
                    break;
                case MocB2VC_Init mocbInit:
                    OnUpdate(mocbInit);
                    break;
                case SMocBMetryMsg mocbMetry:
                    OnUpdate(mocbMetry);
                    break;
                case RC2RKS_Status rcStatus:
                    OnUpdate(rcStatus);
                    break;
                case RC2RKS_Init rcInit:
                    OnUpdate(rcInit);
                    break;
                case SRcControlMetry rcMetryOper:
                    OnUpdate(rcMetryOper);
                    break;
                case SRcDebugMetry rcMetryInit:
                    OnUpdate(rcMetryInit);
                    break;
                case MC2RKS_Status mcStatus:
                    OnUpdate(mcStatus);
                    break;
                case MC2RKS_Init mcInit:
                    OnUpdate(mcInit);
                    break;
                case SMcFastDiagnostics mcFastDiag:
                    OnUpdate(mcFastDiag);
                    break;
                case SMcDisgnostics mcSlowDiag:
                    OnUpdate(mcSlowDiag);
                    break;
                case sBitConfigStatus bitConfigStatus:
                    OnUpdate(bitConfigStatus);
                    break;
                case sBitConfigControl bitConfigControl:
                    OnUpdate(bitConfigControl);
                    break;
                default:
                    // Optionally log or handle unknown types
                    System.Diagnostics.Debug.WriteLine($"[GeneralSaver] Unknown struct type received: {result.GetType().Name}");
                    break;
            }
        }
    }
}
