using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class EmbVersionFinder
    {
        public bool FindVersion<T>(string machineName, DevicesScreen device, 
            cidd_version defualtVersion, EMBVersionStorage storage, agentData? session_data,
            ref T deviceStruct, ClientManager client) where T : struct
        {
            Console.WriteLine($"EmbVersionFinder: Starting version find for machine '{machineName}', device '{device}' default version: {defualtVersion}");
            if (WaitAndCheck(device, session_data, defualtVersion))
            {
                return false; // Version found but is the default one.
            }

            defualtVersion = storage.GetVersion(machineName, device, defualtVersion);
            cidd_version curr_version = defualtVersion;

            //try increment minor version 75 times
            for (int i = 0; i < 7; i++)
            {
                curr_version.VersionMinor++;
                Console.WriteLine($"EmbVersionFinder: Trying version {curr_version} for machine '{machineName}', device '{device}'");
                if (TrySendToDevice(deviceStruct, device, curr_version, session_data, client))
                {
                    storage.SetVersion(machineName, device, curr_version);  
                    return true; // Version found
                }
            }
            curr_version = defualtVersion;
            while (curr_version.VersionMinor-- > 0)
            {
                Console.WriteLine($"EmbVersionFinder: Trying version {curr_version} for machine '{machineName}', device '{device}'");
                if (TrySendToDevice(deviceStruct, device, curr_version, session_data, client))
                {
                    storage.SetVersion(machineName, device, curr_version);
                    return true; // Version found
                }
            }

            curr_version = defualtVersion;
            curr_version.VersionMinor = 0;
            curr_version.VersionMajor++;
            for (int i = 0; i < 7; i++)
            {
                Console.WriteLine($"EmbVersionFinder: Trying version {curr_version} for machine '{machineName}', device '{device}'");
                if (TrySendToDevice(deviceStruct, device, curr_version, session_data, client))
                {
                    storage.SetVersion(machineName, device, curr_version);
                    return true; // Version found
                }
                curr_version.VersionMinor++;
            }
            Console.WriteLine($"EmbVersionFinder: Version not found for machine '{machineName}', device '{device}'");

            return false; //no success.
        }

        private bool TrySendToDevice<T>(T deviceStruct, DevicesScreen device, 
            cidd_version curr_version, agentData? session_data, ClientManager client) where T : struct
        {
            byte[] device_msg = MSGHelper.StructureToByteArray(deviceStruct);
            
            //copy the new version into the struct
            Array.Copy(MSGHelper.StructureToByteArray(curr_version), 0, device_msg, 1, 3);

            T update_device = (MSGHelper.ByteArrayToStruct<T>(device_msg));

            client.SendServerForwardCmdGeneric((int)device, 2, 1, ref update_device, false);

            return WaitAndCheck(device, session_data, curr_version);
        }

        public bool WaitAndCheck(DevicesScreen device, agentData? session_data, cidd_version curr_version, int wait_time=300)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            while (sw.ElapsedMilliseconds < wait_time)
            {
                Thread.Sleep(10); // check every 10 ms (adjust as needed)

                (DateTime? periodicReceived, DateTime? initReplyReceived) = GetLastReceived(device, session_data);
                if (periodicReceived != null || initReplyReceived != null)
                {
                    Console.WriteLine($"EmbVersionFinder: Received response from device '{device}'. Version {curr_version}");
                    return true; // Exit the loop if either timestamp is set
                }
            }
            return false;
        }


        (DateTime?, DateTime?) GetLastReceived(DevicesScreen device, agentData? agent_data)
        {
            DateTime? periodic = null;
            DateTime? initReply = null;
            switch (device)
            {
                case DevicesScreen.MICB:
                    periodic = agent_data?.MicB_Periodic_ReceivedTime;
                    initReply =agent_data?.MicB_Init_Reply_ReceivedTime;
                    break;
                case DevicesScreen.MOCB:
                    periodic = agent_data?.MocB_Periodic_ReceivedTime;
                    initReply =agent_data?.MocB_Init_Reply_ReceivedTime;
                    break;
                case DevicesScreen.RC:
                    periodic = agent_data?.Rc_Periodic_ReceivedTime;
                    initReply = agent_data?.Rc_Init_Reply_ReceivedTime;
                    break;
                case DevicesScreen.MC_FAST:
                    periodic = agent_data?.Mc_Periodic_ReceivedTime;
                    initReply = agent_data?.Mc_Init_Reply_ReceivedTime;
                    break;
                default:    
                    Console.WriteLine($"EmbVersionFinder: Unknown device {device} in GetLastReceived");
                    break;
            }
            return (periodic, initReply);
        }
    }
}
