namespace Testing.Services
{
    public interface IConfiguration
    {
        string DataPath { get; set; }
        string FtpPassword { get; set; }
        string FtpUserName { get; set; }
    }

    public class Configuration : IConfiguration
    {
        public string DataPath { get; set; }
        public string FtpPassword { get; set; }
        public string FtpUserName { get; set; }
    }
}
