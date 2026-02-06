using MSGS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MSGS
{
    public class IniRuleParam
    {
        public eConfigParamType ParamType { get; set; }
        public double Param { get; set; }
    }

    public class IniRule
    {
        public int RuleID { get; set; }
        public int SubSystemID { get; set; }
        public int SubTestID { get; set; }
        public int UnitID { get; set; }
        public int ModuleID { get; set; }
        public int ErrorID { get; set; }
        public int Active { get; set; }
        public int NumOfErrors { get; set; }
        public int Severity { get; set; }
        public int WindowSize { get; set; }
        public IniRuleParam[] Params { get; set; } = new IniRuleParam[4];
        public String Status { get; set; } = String.Empty;
        public String StatusClass 
        {
            get
            {
                return Status switch
                {
                    "Pass" => "bg-success-transparent",
                    "Fail" => "bg-danger-transparent",
                    _ => "bg-gray-500"
                };
            }
        }

    }
}
