using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class AgentConnectionInfo
    {
        public TcpClient? TcpClient { get; }

        /// <summary>
        /// When a agent last accessed this agentNameToRemove (user opened a browser)
        /// </summary>
        public DateTime LastClientBrowserAccessTime { get; set; }

        /// <summary>
        /// Last communication by the agentNameToRemove directly to the server (Agent is alive)
        /// </summary>
        public DateTime LastAgentAccessTime { get; set; }

        public AgentConnectionInfo(TcpClient? client)
        {
            TcpClient = client;
            LastClientBrowserAccessTime = DateTime.UtcNow;
            LastAgentAccessTime = DateTime.UtcNow;
        }
    }
    public class CommRepository
    {
        //store the Communication(TCP) and other data per agent
        private readonly ConcurrentDictionary<string, AgentConnectionInfo> _agentsCommunication = new();
        
        public Action<string, OutgoingMsgsManager.MsgType>? OnAgentAdded;
        public Action<string>? OnAgentRemoved;

        public bool IsAgentConnected(string agentName)
        {
            if (_agentsCommunication.TryGetValue(agentName, out var agentData))
            {
                if (MSGHelper.IsSocketConnected(agentData.TcpClient))
                {
                    return true; // Agent is registered and connected
                }
            }
            return false; // Agent is not registered or not connected
        }
        private void RegisterHeartbeat(string agentName, Action<AgentConnectionInfo> updateAction, 
            string source)
        {
            if (_agentsCommunication.TryGetValue(agentName, out var agentData))
            {
                updateAction(agentData);
            }
            else
            {
                Console.WriteLine($"Major error - {source}");
            }
        }

        /// <summary>
        /// an agent sent a direct msg to the server
        /// </summary>
        /// <param name="agentName"></param>
        internal void AgentMsgReceived(string agentName)
        {
            RegisterHeartbeat(agentName, a => a.LastAgentAccessTime = DateTime.UtcNow, nameof(AgentMsgReceived));
        }

        /// <summary>
        /// a client (open browser) is open and connected to an agent name: agentName
        /// </summary>
        /// <param name="agentName"></param>
        public void ClientBrowserIsAlive(string agentName)
        {
            RegisterHeartbeat(agentName, a => a.LastClientBrowserAccessTime = DateTime.UtcNow, nameof(ClientBrowserIsAlive));
        }

        public TcpClient? GetTCPClientByName(string name)
        {
            if (_agentsCommunication.TryGetValue(name, out var agent))
            {
                return agent.TcpClient;
            }
            return null;
        }

        public void AddAgentByName(string registeredAgentName, TcpClient? client)
        {
            if (client != null) //This happens when an agent is registered - Send Init
            {
                Console.WriteLine($"Adding agent: {registeredAgentName}");
                _agentsCommunication[registeredAgentName] = new AgentConnectionInfo(client);
                OnAgentAdded?.Invoke(registeredAgentName, OutgoingMsgsManager.MsgType.Init);
            }        
            else if (_agentsCommunication.TryAdd( //This only happens for Dummy for testing
                registeredAgentName,
                new AgentConnectionInfo(client)))
            {
                Console.WriteLine($"Size of _agentsCommunication Total: {_agentsCommunication.Count}");
                OnAgentAdded?.Invoke(registeredAgentName, OutgoingMsgsManager.MsgType.Oper);
            }
            else // This happens when a user clicks on a machine from agent selector screen - Send Oper
            {
                OnAgentAdded?.Invoke(registeredAgentName, OutgoingMsgsManager.MsgType.Oper);
            }
        }

        /// <summary>a
        /// get the names of all agents that has minimum of one agent connected
        /// </summary>
        /// <returns></returns>
        internal List<string> GetActiveAgentsByClientsOrDirectToServer(
            Func<AgentConnectionInfo, DateTime> timestampSelector)
        {
            const int numOfSecondsSpan = 31; //31 seconds
            var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(numOfSecondsSpan);

            return _agentsCommunication.Where(
                agent => timestampSelector(agent.Value) >= cutoff)
            .Select(agent => agent.Key).ToList();
        }


        /// <summary>
        /// Removes clients that have not sent a message for 1 minute
        /// </summary>
        internal List<string> RemoveOldAgents(List<string> activeAgents)
        {
            List<string> removed_agents = new List<string>();
            foreach (var agent in _agentsCommunication)
            {
                if (agent.Key == "Dummy for testing")// || agent.Key == "Lab D" || agent.Key == "Basement" || agent.Key == "DUMMY - FOR TEST")
                    continue; // Skip these agents

                if (ShouldRemoveAgent(agent, activeAgents))
                {
                    if(TryRemoveAgent(agent.Key))
                    {
                        removed_agents.Add(agent.Key);
                    }
                }
            }
            return removed_agents;
        }

        private bool ShouldRemoveAgent(KeyValuePair<string, AgentConnectionInfo> agent, 
            List<string> activeAgents)
        {
            return !MSGHelper.IsSocketConnected(agent.Value.TcpClient)
                || !activeAgents.Contains(agent.Key);
        }

        
        public bool TryRemoveAgent(string agentKey)
        {
            if (_agentsCommunication.TryRemove(agentKey, out var removedAgent))
            {
                try
                {
                    Console.WriteLine($"🕒 Client timed out. Removing: {agentKey}");
                    if (removedAgent.TcpClient != null)
                    {
                        removedAgent.TcpClient.Close();
                    }
                    OnAgentRemoved?.Invoke(agentKey);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Error closing agent '{agentKey}': {ex.Message}");
                }

                
                return true;
            }
            return false;
        }


        public List<string> GetAllAgentsNames()
        {
            return _agentsCommunication.Keys.ToList();
        }

        public void Stop()
        {
            foreach (var kvp in _agentsCommunication)
            {
                var client = kvp.Value.TcpClient;
                if (client != null)
                {
                    client.Close();
                }
            }

            _agentsCommunication.Clear();
        }
    }
}
