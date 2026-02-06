using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FSMSGS
{
    public class TCPSettings
    {
        public required string Host { get; set; }
        public required int Port { get; set; }

        public required bool IsDebugMode { get; set; }

        public required bool Yaniv_Local { get; set; }

        public string ProgramVersion { get; set; } = string.Empty;
    }

}
