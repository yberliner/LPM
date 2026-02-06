using ForsightTester.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardModel;

namespace Index1
{
    public class Index1Service
    {
        private List<UDPStatus> UDPStatusData = new List<UDPStatus>()
        {
            new UDPStatus {
                id = 1,
                title = "UDP Packages sent",
                icon = "ti ti-package-export",
                iconclass ="bg-primary",
                value = "854" ,
                status ="Dropped ",
                statusclass ="text-success",
                statusdata ="2.56%",
//                statusicon ="ti ti-arrow-narrow-up", 
                IsRounded=true,
                MainBgImg=true,
            },
            new UDPStatus {
                id = 2,
                title = "UDP Packages received",
                icon = "ti ti-package-import",
                iconclass ="bg-primary1",
                value = "31,876" ,
                status ="Dropped ",
                statusclass ="text-success",
                statusdata ="0.34%",
//                statusicon ="ti ti-arrow-narrow-up",
                IsRounded=true,
                MainBgImg=true,
            },
            new UDPStatus {
                id = 3,
                title = "UDP Packages received",
                icon = "ti ti-package-import",
                iconclass ="bg-primary1",
                value = "31,876" ,
                status ="Dropped ",
                statusclass ="text-success",
                statusdata ="0.34%",
//                statusicon ="ti ti-arrow-narrow-up",
                IsRounded=true,
                MainBgImg=true,
            }
        };

        public List<UDPStatus> GetUDPData()
        {
            return UDPStatusData;
        }

        // Sales Overview Start //
    }
}