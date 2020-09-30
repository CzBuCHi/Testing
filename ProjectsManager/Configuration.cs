using JetBrains.Annotations;

namespace ProjectsManager
{
    public class Configuration
    {
        [NotNull]
        public readonly string DiffPath = @"c:\ProjectsManager\Diff";

        [NotNull]
        public readonly string FtpPassword = "";

        [NotNull]
        public readonly string FtpUserName = "";

        [NotNull]
        public readonly string LocalPath = @"c:\ProjectsManager\Cache";

        [NotNull]
        public readonly string LoginName = "marek.buchar";

        [NotNull]
        public readonly string Password = "";

        [NotNull]
        public readonly string ServiceUrl = "";

        [NotNull]
        public readonly string TortoiseGitMerge = @"c:\Program Files\TortoiseGit\bin\TortoiseGitMerge.exe";
    }
}
