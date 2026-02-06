using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace MSGS
{
    public class TCPMessageSender
    {
        CommRepository _commRepository;
        AgentsRepository _agents;

        public TCPMessageSender(CommRepository commRepository, AgentsRepository agents)
        {
            _commRepository = commRepository;
            _agents = agents;
        }

        public bool Send(string agentName, TcpClient client, ref byte[] message)
        {
            try
            {
                if (client == null)
                {
                    Debug.WriteLine("❌ TcpClient is null.");
                    return false;
                }

                if (!MSGHelper.IsSocketConnected(client))
                {
                    Debug.WriteLine("❌ TcpClient is not connected.");
                    if (_commRepository.TryRemoveAgent(agentName))
                    {
                        _agents.DeleteAgents(new List<string> { agentName });
                    }
                    return false;
                }

                // 🔧 Convert struct to byte array
                byte[] uncompressed = message;// MSGHelper.StructureToByteArray(message);
                int originalSize = uncompressed.Length;

                // 🔄 Compress using ZstdNet
                byte[] compressed;
                using (var compressor = new ZstdNet.Compressor())
                {
                    compressed = compressor.Wrap(uncompressed);
                }

                int compressedSize = compressed.Length;

                // 📨 Send header + payload: [originalSize][compressedSize][compressedData]
                NetworkStream stream = client.GetStream();
                BinaryWriter writer = new BinaryWriter(stream);

                writer.Write(originalSize);       // 4 bytes
                writer.Write(compressedSize);     // 4 bytes
                writer.Write(compressed);         // compressed payload

                //Debug.WriteLine($"✅ Sent {compressedSize} compressed bytes (from {originalSize}) to client '{agentName}'.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ Error sending message to '{agentName}': {ex.Message}");
                return false;
            }
        }

    }
}
