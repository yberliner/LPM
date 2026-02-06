using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using FSMSGS;
using Microsoft.Extensions.Options;

namespace MSGS
{
    public class TCPEngine
    {
        
        private readonly object _sendLock = new();
        
        private readonly TCPMessageSender _tcpSender;

        private readonly TCPMessageListener _tcpListener;

        private readonly CommRepository _commRepository;

        public TCPEngine(AgentsRepository agents, CommRepository commRepository,
            IOptions<TCPSettings> tcpSettingsOptions)
        {
            _commRepository = commRepository;

            var tcpSettings = tcpSettingsOptions.Value;

            // 👇 Print the host and port being used
            Console.WriteLine($"Starting TCP Listener on {tcpSettings.Host}:{tcpSettings.Port}");
            Console.WriteLine($"Debug mode is: {tcpSettings.IsDebugMode}");
            Console.WriteLine($"Yaniv local is: {tcpSettings.Yaniv_Local}");

            // 👇 Build the TCP listener with the proper dispatch method
            _tcpListener = new TCPMessageListener(tcpSettings.Host, tcpSettings.Port,
                agents, _commRepository);

            _tcpSender = new TCPMessageSender(_commRepository, agents);

            //_isYanivLocal = (Environment.MachineName == "DESKTOP-NR3351E" || Environment.MachineName == "OPHIR-DELL-I9");
            
                       
        }

        public void Start()
        {
            _tcpListener.Start(); 
        }

        internal void SendMsg(ref byte[] msg, string agentName)
        {
            if (agentName == "Dummy for testing")// || agentName == "Lab D" || agentName == "Basement" || agentName == "DUMMY - FOR TEST")
                return; // Skip these agents

            lock (_sendLock)
            {
                try
                {
                    TcpClient? tcpClient = _commRepository.GetTCPClientByName(agentName);
                    if (tcpClient != null)
                    {
                        _tcpSender.Send(agentName, tcpClient, ref msg);
                    }
                    else
                    {
                        Console.WriteLine("Error SendMsg - agent name is null or tcp is invalid");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error in SendMsg: {e}");
                }
            }
        }

    }
}
