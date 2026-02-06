using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;

namespace MSGS
{
    public class QueuedBuffer
    {
        public required byte[] Buffer { get; set; }
        public required string AgentName { get; set; }
        public DateTime EnqueuedTimeUtc { get; set; } // Time when the message was enqueued
    }

    public class TCPMessageListener
    {
        //C# listner on the port (3068)
        private readonly TcpListener _listener;

        private Thread? _acceptThread;
        private volatile bool _isRunning;

        //For the thread to process the message after TCP thread.
        private readonly ConcurrentQueue<QueuedBuffer> _bufferQueue = new();
        private readonly AutoResetEvent _queueSignal = new(false);
        private Thread? _processorThread;


        // singleton classes
        private readonly AgentsRepository _agentsRepository;
        private readonly CommRepository _commRepository;

        public TCPMessageListener(string ip, int port,
            AgentsRepository agents, CommRepository commRepository)
        {
            _agentsRepository = agents;
            _listener = new TcpListener(IPAddress.Parse(ip), port);
            _commRepository = commRepository;
        }

        public void Start()
        {
            _isRunning = true;
            _listener.Start();

            _acceptThread = new Thread(AcceptClients)
            {
                IsBackground = true,
                Priority = ThreadPriority.Normal
            };
            _acceptThread.Start();

            _processorThread = new Thread(ProcessingLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Normal
            };
            _processorThread.Start();

            Console.WriteLine("✅ Server started and ready to accept clients.");
        }

        public void Stop()
        {
            _isRunning = false;
            _listener.Stop();
        }

        private void AcceptClients()
        {
            while (_isRunning)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    string clientId = client.Client?.RemoteEndPoint?.ToString() ?? "UnknownClient";

                    Console.WriteLine($"➡️ Client connected: {clientId}");

                    new Thread(() => HandleClient(client, clientId))
                    {

                        IsBackground = true,
                        Priority = ThreadPriority.Normal   // or Highest
                    }.Start();

                    //ThreadPool.QueueUserWorkItem(_ => HandleClient(client, clientId));
                }
                catch (SocketException ex)
                {
                    Console.WriteLine($"❌ Accept error: {ex.Message}");
                }
            }
        }


        private void HandleClient(TcpClient client, string clientId)
        {
            var sw = new Stopwatch();
            long iterations = 0;
            
            string registeredAgentName = "";
            byte[] headerBuffer = new byte[8];
            byte[] compressedBuffer = new byte[2048];    // ✅ Max expected compressed size
            byte[] decompressedBuffer = new byte[10000]; // ✅ Preallocate max decompressed size

            try
            {
                var stream = client.GetStream();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                KeepAliveHelper.EnableFastKeepAlive(client.Client);

                using var decompressor = new ZstdNet.Decompressor();
                using var reader = new BinaryReader(stream);
                LogTimer _logTimer = new LogTimer(10);

                while (_isRunning && client.Connected)
                {
                    if(!ReadExact(stream, headerBuffer, 8))
                    {
                        Console.WriteLine($"❌ ReadExact failed for {clientId}. Connection closed or incomplete data.");
                        break; // connection closed or error
                    }
                    sw.Start();

                    int originalSize = BitConverter.ToInt32(headerBuffer, 0);
                    int compressedSize = BitConverter.ToInt32(headerBuffer, 4);


                    // 🧱 Read compressed payload
                    if (!TryReadBytes(reader, compressedBuffer, compressedSize, out int read))
                    {
                        Console.WriteLine($"TCP Listner - mismatch. read: {read}. compressedSize: {compressedSize}.");
                        break;
                    }

                    // 🔄 Decompress directly into preallocated buffer
                    byte[] decompressed;
                    try
                    {
                        decompressed = decompressor.Unwrap(compressedBuffer.AsSpan(0, compressedSize));//.ToArray());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Decompression failed ({clientId}): {ex.Message}");
                        break;
                    }

                    if (decompressed.Length != originalSize)
                    {
                        Console.WriteLine($"❌ Size mismatch: Expected {originalSize}, got {decompressed.Length}");
                        break;
                    }

                    // ✅ Extract agent name only once
                    if (string.IsNullOrEmpty(registeredAgentName))
                    {
                        ServerMsg msg = MSGHelper.ByteArrayToStruct<ServerMsg>(decompressed);
                        registeredAgentName = Encoding.UTF8.GetString(msg.agent_name).TrimEnd('\0');
                        _agentsRepository.AddAgent(registeredAgentName);
                        _commRepository.AddAgentByName(registeredAgentName, client);

                        Console.WriteLine($"✅ TCPListener Client handler started for {registeredAgentName}.");
                        if (!MSGHelper.IsArrayValidAsMsg<ServerMsg>(decompressed))
                        {
                            Console.WriteLine($"Major error exception. Invalid msg size of agent: {registeredAgentName}");
                        }

                    }

                    // ✅ Enqueue without extra allocations
                    _bufferQueue.Enqueue(new QueuedBuffer
                    {
                        Buffer = decompressed, // if you want to avoid copy: pass the same buffer as Memory<byte>
                        AgentName = registeredAgentName,
                        EnqueuedTimeUtc = DateTime.UtcNow
                    });


                    // ✅ Log every 10 minutes
                    if (_logTimer.TimeToLog())
                    {
                        agentData? data = _agentsRepository.GetClientAgentData(registeredAgentName);
                        Console.WriteLine($@"
                                  [Perf] HandleClient loop Agent: {registeredAgentName}:
                                  Iterations: {iterations}
                                  busyTime: {sw.ElapsedMilliseconds / 1000} s  
                                  Queue size: {_bufferQueue.Count}
                                  non script msgs: {data?.errorState._num_of_non_scriptes_msgs}
                                  script msgs: {data?.errorState._num_of_scriptes_msgs}");
                    }

                    _queueSignal.Set();

                    sw.Stop();
                    iterations++;

                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Client TCPListener exception handler error. Agent Name: {registeredAgentName}. ID: ({clientId}): Message: {ex.Message}");
            }
            try
            {
                Console.WriteLine($"❌ TCPListener Client disconnected. Name: {registeredAgentName}");
                _commRepository.TryRemoveAgent(registeredAgentName);
                _agentsRepository.DeleteAgents(new List<string> { registeredAgentName });
            }
            catch (Exception e)
            {
                Console.WriteLine($"❌ TCPListener Error removing agent {registeredAgentName}: {e.Message}");
            }
            
            
        }

        private bool ReadExact(NetworkStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read == 0)
                {
                    Console.WriteLine($"❌ ReadExact: Connection closed. Expected {count} bytes, read {offset} bytes.");
                    return false; // connection closed
                }
                offset += read;
            }
            return true;
        }


        /// <summary>Reads exactly `size` bytes into `buffer`.</summary>
        private static bool TryReadBytes(BinaryReader reader, byte[] buffer, int size, out int totalRead)
        {
            totalRead = 0;
            try
            {
                while (totalRead < size)
                {
                    int bytesRead = reader.Read(buffer, totalRead, size - totalRead);
                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"TryReadBytes: Connection closed. Received {totalRead}/{size} bytes.");
                        return false; // connection closed
                    }

                    totalRead += bytesRead;
                }
                return true; // ✅ only returns true if full size read
            }
            catch (Exception ex)
            {
                totalRead = 0;
                Console.WriteLine($"Exception TryReadBytes: {ex}");
                return false;
            }
        }


        private void ProcessingLoop()
        {
            var sw = new Stopwatch();
            long iterations = 0;

            DateTime loopStartTime = DateTime.UtcNow;   // ✅ Track function start time
            DateTime lastLogTime = DateTime.UtcNow;
            double maxElapsedMs = 0;
            int over200msCount = 0;
            LogTimer _logTimer = new LogTimer(10);

            while (_isRunning)
            {
                _queueSignal.WaitOne(); // Wait for a signal from the producer

                sw.Start();

                while (_bufferQueue.TryDequeue(out var item))
                {
                    try
                    {
                        // Check elapsed time in queue
                        var elapsedMs = (DateTime.UtcNow - item.EnqueuedTimeUtc).TotalMilliseconds;

                        //process the message
                        ServerMsg msg = MSGHelper.ByteArrayToStruct<ServerMsg>(item.Buffer);
                        _agentsRepository.DealWithServerMsg(ref msg, item.AgentName); // ✅ Use AgentName here

                        //print to log if the message was in queue for more than 200ms
                        PrintToLogLongWaitingTime(ref lastLogTime, ref maxElapsedMs, ref over200msCount, elapsedMs);

                         iterations++;

                        // ✅ Log every 10 minutes
                        if (_logTimer.TimeToLog())
                        {
                            double totalRuntimeSec = (DateTime.UtcNow - loopStartTime).TotalSeconds;  // ✅ Calculate total runtime
                            Console.WriteLine(
                            $"[Perf] Processing loop: Iterations: {iterations} | Runtime: {totalRuntimeSec:F1} s | busyTime: {sw.ElapsedMilliseconds / 1000} s| Agent: {item.AgentName}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Error processing queued buffer: {ex.Message}");
                    }
                }
                sw.Stop();
            }
        }

        /// <summary>
        /// print to he log if the message was in queue for more than 200ms
        /// </summary>
        /// <param name="lastLogTime"></param>
        /// <param name="maxElapsedMs"></param>
        /// <param name="over200msCount"></param>
        /// <param name="elapsedMs"></param>
        private static void PrintToLogLongWaitingTime(
            ref DateTime lastLogTime,
            ref double maxElapsedMs,
            ref int over200msCount,
            double elapsedMs)
        {
            if (elapsedMs > 200)
            {
                // Track stats instead of logging immediately
                over200msCount++;
                if (elapsedMs > maxElapsedMs)
                    maxElapsedMs = elapsedMs;
            }


            // Check if at least 1 second has passed since last log
            if ((DateTime.UtcNow - lastLogTime).TotalSeconds >= 1)
            {
                if (over200msCount > 0)
                {
                    Console.WriteLine(
                        $"⚠️ {over200msCount} messages spent >200ms in queue in the last second. Max elapsed: {maxElapsedMs:F0} ms");
                }
                // Reset counters
                lastLogTime = DateTime.UtcNow;
                maxElapsedMs = 0;
                over200msCount = 0;
            }
        }
    }
}
