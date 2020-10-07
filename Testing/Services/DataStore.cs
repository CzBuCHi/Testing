using System.Collections.Generic;
using Testing.ProjectsManagerService;

namespace Testing.Services
{
    public class DataStore
    {
        public Dictionary<string, AccessLogInfo> AccessLogs { get; set; }
        public Dictionary<string, HostingWebApplicationInfo> Applications { get; set; }
        public Dictionary<string, string> EasyWebVersions { get; set; }
        public Dictionary<int, HostingWebServerInfo> Servers { get; set; }
    }
}
