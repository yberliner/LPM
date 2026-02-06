using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MSGS
{
    public class AgentsRepository
    {
        //map between the name of agent and its new_agent
        private readonly ConcurrentDictionary<string, agentData> _agents = new();
        private readonly MessageDispatch _dispatcher;
        
        public AgentsRepository() 
        {
            _dispatcher = new();
        }
        public List<agentData> GetAgents => _agents.Values.ToList();

        public MessageDispatch Dispatcher => _dispatcher;

        /// <summary>
        /// Do the actual from ServerMsg to an agent update
        /// </summary>
        /// <param name="msg"></param>
        public bool DealWithServerMsg(ref ServerMsg msg, string registeredAgentName)
        {
            try
            {
                if (msg.cmd == Cmd.Register_agent)
                {
                    if(TryAddOrUpdateAgent(msg.buffer, msg.size_of_data, registeredAgentName))
                    {
                        return true;
                    }
                }
                
                _agents.TryGetValue(registeredAgentName, out agentData? data);
                if (data != null)
                {
                    Dispatcher.DispatchMessage(ref msg, ref registeredAgentName, data);
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception in dispatch msg. {e.ToString()}");      
            }
            return true;
            
        }

        public agentData? GetClientAgentData(string agentName)
        {
            if (!_agents.ContainsKey(agentName) || _agents[agentName] == null)
            {
                //Console.WriteLine($"⚠️ Agent {agentName} not found in repository.");
                return null;
            }
            Debug.Assert(agentName == _agents[agentName].AgentName);
            return _agents[agentName];
        }

        private string? GetAgentProperty(string agentName, string propertyName)
        {
            try
            {
                if (_agents.TryGetValue(agentName, out agentData? data) && data != null)
                {
                    if (data.agentJson.ValueKind == JsonValueKind.Object &&
                        data.agentJson.EnumerateObject().Any())
                    {
                        var agentElement = data.agentJson.GetProperty("agent");
                        if (agentElement.TryGetProperty(propertyName, out var prop))
                        {
                            return prop.GetString();
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        public string? GetClientIDD(string agentName) => GetAgentProperty(agentName, "IDD");

        public string? GetClientVersion(string agentName) => GetAgentProperty(agentName, "version");

        public string? GetClientFW_emb(string agentName) => GetAgentProperty(agentName, "emb_fw");

        public string? GetClientHost(string agentName)
        {
            try
            {
                if (_agents.TryGetValue(agentName, out agentData? data) && data != null)
                {
                    if (data.agentJson.ValueKind == JsonValueKind.Object &&
                        data.agentJson.EnumerateObject().Any())
                    {
                        var agentElement = data.agentJson.GetProperty("agent");
                        if (agentElement.TryGetProperty("connection", out var connectionElement) &&
                            connectionElement.ValueKind == JsonValueKind.Object &&
                            connectionElement.TryGetProperty("host", out var hostElement))
                        {
                            return hostElement.GetString();
                        }
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Delete agents that do not have tcp communication
        /// </summary>
        /// <param name="removedAgents"></param>
        public void DeleteAgents(List<string> removedAgents)
        {
            foreach (string agentName in removedAgents)
            {
                Console.WriteLine($"Agent repositoty: Deleting agent: {agentName} from repository.");
                //if (!_agents.TryGetValue(agentName, out agentData? existing_agent))
                //{
                //    if (existing_agent != null)
                //    {
                //        existing_agent.AgentName = string.Empty;
                //    }                    
                //}
                _agents.TryRemove(agentName, out _);
            }
        }

        public void AddAgent(string agentToAdd)
        {
            _agents.TryAdd(agentToAdd, new agentData { AgentName = agentToAdd });
            _agents[agentToAdd].startup_msg_sent = false;
            Console.WriteLine($"Stored agent: {agentToAdd} from AddAgent");
        }

        private bool TryAddOrUpdateAgent(byte[] buffer, int len, string registeredAgentName)
        {
            try
            {
                bool ret_val = false;
                string jsonString = Encoding.UTF8.GetString(buffer, 0, len);
                using JsonDocument doc = JsonDocument.Parse(jsonString);

                JsonElement root = doc.RootElement.Clone(); // Clone to persist
                
                agentData new_agent = new() { agentJson = root, AgentName = registeredAgentName };
                
                // Log just once.
                if (!_agents.TryGetValue(registeredAgentName, out agentData? existing_agent) ||
                    existing_agent.agentJson.ValueKind == JsonValueKind.Undefined)
                {
                    _agents[registeredAgentName] = new_agent;
                    ret_val = true;
                    string? version = GetClientVersion(registeredAgentName);
                    Console.WriteLine($"✅ Stored agent: {registeredAgentName}. Version: {version ?? " < unknown > "}");
                }
                else 
                {
                    existing_agent.agentJson = root;
                }
                return ret_val;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to parse agent JSON: {ex.Message}");
                return false;
            }
        }
    }
}
