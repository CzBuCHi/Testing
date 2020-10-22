using System.Collections.Generic;
using Testing.ProjectsManagerService;

namespace Testing.Services
{
    public interface IDataStore
    {
        Dictionary<string, AccessLogInfo> AccessLogs { get; set; }
        Dictionary<string, HostingWebApplicationInfo> Applications { get; set; }
        Dictionary<string, string> EasyWebVersions { get; set; }
        Dictionary<int, HostingWebServerInfo> Servers { get; set; }
    }

    public class DataStore : IDataStore
    {
        public Dictionary<string, AccessLogInfo> AccessLogs { get; set; }
        public Dictionary<string, HostingWebApplicationInfo> Applications { get; set; }
        public Dictionary<string, string> EasyWebVersions { get; set; }
        public Dictionary<int, HostingWebServerInfo> Servers { get; set; }
    }
}
