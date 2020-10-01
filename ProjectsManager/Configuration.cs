using JetBrains.Annotations;

namespace ProjectsManager
{

    public class Configuration
    {
        public string DiffPath { get; set; }
        public string FtpPassword { get; set; }
        public string FtpUserName { get; set; }
        public string LocalPath { get; set; }
        public string LoginName { get; set; }
        public string Password { get; set; }
        public string ServiceUrl { get; set; }
        public string TortoiseGitMerge { get; set; }
    }
}
