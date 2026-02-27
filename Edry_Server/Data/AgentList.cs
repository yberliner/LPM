using CardModel;
using FSMSGS;
using Microsoft.Extensions.Options;
using MSGS;
using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Metadata.Ecma335;
using System.Security.Permissions;
using static System.Net.Mime.MediaTypeNames;

namespace LPM.Data
{
    public class AgentInfo
    {
        public string? Name { get; set; }
        public string? IP { get; set; }
        public string? IDDVersion { get; set; }
        public string? MiccbVersion { get; set; }
        public string? MocbVersion { get; set; }
        public string? RcVersion { get; set; }
        public string? McVersion { get; set; }
        public string? LastUpdateTime { get; set; }
        public string?  AgentVersion { get; set; }

        public (string Text, string CssClass) Miccb => GetBadge(MiccbVersion);
        public (string Text, string CssClass) Mocb => GetBadge(MocbVersion);
        public (string Text, string CssClass) Rc => GetBadge(RcVersion);
        public (string Text, string CssClass) Mc => GetBadge(McVersion);
        public (string Text, string CssClass) Agent()
        {
            if (float.TryParse(AgentVersion, NumberStyles.Float, CultureInfo.InvariantCulture, out float version))
            {
                return version >= 0.44f
                    ? (AgentVersion, "bg-success-transparent")
                    : (AgentVersion, "bg-danger-transparent");
            }
            return GetBadge(AgentVersion);
        }

        private (string Text, string CssClass) GetBadge(string? version)
        {
            bool hasValue = !string.IsNullOrWhiteSpace(version);
            return (
                Text: hasValue ? version! : "Fail",
                CssClass: hasValue ? "bg-success-transparent" : "bg-danger-transparent"
            );
        }
    }


    public class AgentList
    {
        private readonly bool _isDebugMode;
        private readonly AgentsRepository _agents;
        private readonly CommRepository _commRepo;

        public List<TableText> AgentHeaders = new List<TableText>
        {
            new TableText { Title = "Name" },
            new TableText { Title = "IP" },
            new TableText { Title = "Agent<br/>Version" },
            //new TableText { Title = "IDD<br/>Version" },
            new TableText { Title = "MICCB<br/>Version" },
            new TableText { Title = "MOCB<br/>Version" },
            new TableText { Title = "RC<br/>Version" },
            new TableText { Title = "MC<br/>Version" },
            new TableText { Title = "Last Update" }
        };
        public List<TableText> GetAgentHeadersData() => AgentHeaders;

        private List<AgentInfo> DummyAgentInfoData = new List<AgentInfo>()
        {

            new AgentInfo {
                Name="Dummy for testing",
                IP="192.168.32.1",
                AgentVersion = "0.44",
                IDDVersion="2.3",
                MiccbVersion="1.03",
                MocbVersion="1.04",
                RcVersion="1.01",
                McVersion="1.01",
                LastUpdateTime = "2025-08-15 10:30"
             }
            //new AgentInfo {
            //    Name="Lab D",
            //    IP="192.168.17.12",
            //    AgentVersion = "0.3",
            //    IDDVersion="2.02",
            //    MiccbVersion="1.03",
            //    MocbVersion="",
            //    RcVersion="",
            //    McVersion="1.02",
            //    LastUpdateTime = "2025-05-16 10:35"
            // },
            //new AgentInfo {
            //    Name="Basement",
            //    IP="192.168.17.13",
            //    AgentVersion = "0.3",
            //    IDDVersion="2.12",
            //    MiccbVersion="1.04",
            //    MocbVersion="1.05",
            //    RcVersion="1.02",
            //    McVersion="1.02",
            //    LastUpdateTime = "2025-05-16 21:30"
            // }
        };

        public AgentList(
            AgentsRepository agents, 
            CommRepository commRepo,
            IOptions<TCPSettings> tcpSettingsOptions)
        {
            _commRepo = commRepo;
            _agents = agents;
            _isDebugMode = tcpSettingsOptions.Value.IsDebugMode;
        }

        public List<AgentInfo> GetAgentData()
        {
            //the temp list for demo only.
            List<AgentInfo> infoes =  new();

            if (_isDebugMode)
            {
                //COMMENT THIS LINE IF YOU WANT TO REMOVE DUMMY AGENTS
                infoes.AddRange(DummyAgentInfoData);
            }

            //if we have data then show real data.
            List<agentData> agents = _agents.GetAgents;
            if (agents.Count > 0)
            {
                foreach (agentData agent in agents)
                {
                    if (agent.AgentName == "Dummy for testing")// || agent.AgentName == "Lab D" || agent.AgentName == "Basement")
                        continue; // Skip these agents

                    if (!_commRepo.IsAgentConnected(agent.AgentName))
                    {
                        if (_commRepo.TryRemoveAgent(agent.AgentName))
                        {
                            _agents.DeleteAgents(new List<string> { agent.AgentName });
                        }
                        continue;
                    }
                    var info = new AgentInfo
                    {
                        AgentVersion = _agents.GetClientVersion(agent.AgentName),
                        Name = agent.AgentName,
                        IDDVersion = _agents.GetClientIDD(agent.AgentName),
                        MiccbVersion = GetMostRecentVersion(agent,
                            a => a.MicB_Init_Reply_ReceivedTime,
                            a => a.MicB_Periodic_ReceivedTime,
                            a => a.micb_init_reply.header.VersionIdd,
                            a => a.micb_periodic_status.header.VersionIdd),

                        MocbVersion = GetMostRecentVersion(agent,
                            a => a.MocB_Init_Reply_ReceivedTime,
                            a => a.MocB_Periodic_ReceivedTime,
                            a => a.mocb_init_reply.header.VersionIdd,
                            a => a.mocb_periodic_status.header.VersionIdd),

                        RcVersion = GetMostRecentVersion(agent,
                            a => a.Rc_Init_Reply_ReceivedTime,
                            a => a.Rc_Periodic_ReceivedTime,
                            a => a.rc_init_reply.header.VersionIdd,
                            a => a.rc_periodic_status.header.VersionIdd),

                        McVersion = GetMostRecentVersion(agent,
                            a => a.Mc_Init_Reply_ReceivedTime,
                            a => a.Mc_Periodic_ReceivedTime,
                            a => a.mc_init_reply.header.VersionIdd,
                            a => a.mc_periodic_status.header.VersionIdd),

                        LastUpdateTime = GetLastUpdateTime(agent),

                        IP = (_commRepo.GetTCPClientByName(agent.AgentName)?.Client?.
                            RemoteEndPoint as IPEndPoint)?.Address.ToString()



                    };
                    infoes.Add(info);
                }
            }

            return infoes;
        }

        private string? GetMostRecentVersion(
            agentData agent,
            Func<agentData, DateTime?> initTimeGetter,
            Func<agentData, DateTime?> operTimeGetter,
            Func<agentData, cidd_version> initVersionGetter,
            Func<agentData, cidd_version> operVersionGetter)
        {
            DateTime? initTime = initTimeGetter(agent);
            DateTime? operTime = operTimeGetter(agent);

            if (initTime == null && operTime == null)
                return null;

            return (initTime != null)
                ? FormatVersion(initVersionGetter(agent))
                : FormatVersion(operVersionGetter(agent));
        }

        private static string? GetLastUpdateTime(agentData agent)
        {
            var timestamps = new DateTime?[]
            {
                agent.MicB_Init_Reply_ReceivedTime,
                agent.MicB_Periodic_ReceivedTime,
                agent.MocB_Init_Reply_ReceivedTime,
                agent.MocB_Periodic_ReceivedTime,
                agent.Rc_Init_Reply_ReceivedTime,
                agent.Rc_Periodic_ReceivedTime,
                agent.Mc_Init_Reply_ReceivedTime,
                agent.Mc_Periodic_ReceivedTime
            };

            var latest = timestamps
                .Where(t => t.HasValue)
                //.Select(t => DateTime.SpecifyKind(t!.Value, DateTimeKind.Utc))  // ensure it's treated as UTC
                .OrderByDescending(t => t)
                .FirstOrDefault();

            if (latest == default)
                return null;

            // Convert to Israel time
            //var israelTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Israel Standard Time");
            //var israelTime = TimeZoneInfo.ConvertTimeFromUtc(latest, israelTimeZone);

            //we moved to forsight tester which is in UTC+2 (Israel Standard Time)
            return latest?.ToString("yyyy-MM-dd HH:mm");
            //return israelTime.ToString("yyyy-MM-dd HH:mm:ss");
        }


        public static string FormatVersion(cidd_version version)
        {
            return $"{version.VersionMajor}.{version.VersionMinor}.{version.VersionPatch}";
        }


    }
}
