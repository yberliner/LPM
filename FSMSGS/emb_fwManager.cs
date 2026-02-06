using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MSGS;
using System;
using System.IO;

namespace FSMSGS
{
    public class EmbFwManager
    {
        private readonly AgentsRepository _agentsRepository;

        public EmbFwManager(AgentsRepository agentsRepository)
        {
            _agentsRepository = agentsRepository ?? throw new ArgumentNullException(nameof(agentsRepository));
        }

        public string? GetValue(string agentName, string category, string subCategory= "version")
        {
            string? wholeFWFile = _agentsRepository.GetClientFW_emb(agentName);

            return GetValueEmb(wholeFWFile, category, subCategory);
        }

        /// <summary>
        /// Returns the value of a given sub-category (key) under a given category (section)
        /// from an embedded firmware .ini file content string.
        /// Example: category = "MicB_Fast", subCategory = "version" → "2.5.4.40"
        /// Returns empty string if not found.
        /// </summary>
        private string? GetValueEmb(string? wholeFileContent, string category, string subCategory)
        {
            if (string.IsNullOrWhiteSpace(wholeFileContent) ||
                string.IsNullOrWhiteSpace(category) ||
                string.IsNullOrWhiteSpace(subCategory))
                return string.Empty;

            using var reader = new StringReader(wholeFileContent);
            string? line;
            bool inTargetSection = false;
            string targetSectionHeader = $"[{category}]";

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                // Skip empty lines or comments
                if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith("//"))
                    continue;

                // Section header?
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    inTargetSection = string.Equals(line, targetSectionHeader, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inTargetSection)
                {
                    // New section begins → stop searching
                    if (line.StartsWith("[")) break;

                    int eqIndex = line.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string key = line[..eqIndex].Trim();
                        string value = line[(eqIndex + 1)..].Trim();

                        if (string.Equals(key, subCategory, StringComparison.OrdinalIgnoreCase))
                            return value;
                    }
                }
            }

            return string.Empty;
        }
    }

}
