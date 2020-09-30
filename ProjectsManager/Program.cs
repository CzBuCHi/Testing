using System;
using System.Diagnostics;
using System.IO;
using Centaur.Client.Wcf;
using JetBrains.Annotations;
using ProjectsManager.ProjectsManagerService;

namespace ProjectsManager
{
    internal class Program
    {
        private const string ApplicationIdentifier = "test1.cstech.cz";

        [NotNull]
        public Configuration Configuration = new Configuration();

        private static void Main() {
            Program program = new Program();
            program.MainIntance();
            Console.ReadLine();
        }

        private void MainIntance() {
            if (!Directory.Exists(Configuration.LocalPath)) {
                Directory.CreateDirectory(Configuration.LocalPath);
            }

            if (!Directory.Exists(Configuration.DiffPath)) {
                Directory.CreateDirectory(Configuration.DiffPath);
            }


            HostingWebApplicationInfoComplex info = new HostingWebApplicationInfoComplex() {
                ApplicationInfo = new HostingWebApplicationInfo { Identifier = ApplicationIdentifier, },
                ServerInfo = new HostingWebServerInfo { BaseAddress = "127.0.0.1", },
                Bindings = new[] { new HostingWebApplicationBindingInfo { IsHttps = false, Domain = ApplicationIdentifier, }, },
            };

            //using (ProjectsManagerServiceClient client = ServiceInitializer.ClientInitSecureAuth<ProjectsManagerServiceClient, IProjectsManagerService>(Configuration.ServiceUrl, Configuration.LoginName, Configuration.Password)) {
            //    Debug.Assert(client != null, nameof(client) + " != null");
            //    info = client.GetWebApplicationInfoComplex(ApplicationIdentifier);
            //    Debug.Assert(info != null, nameof(info) + " != null");
            //}



            using (ProjectManager projectManager = new ProjectManager(info, Configuration)) {
                projectManager.Download();
                //projectManager.Watch = true;

                Console.WriteLine("Done");
                Console.ReadLine();
            }


            //string arguments = $"/O /T /R=ftp://{FtpUserName}:{FtpPassword}@{info.ServerInfo.BaseAddress}/{info.ApplicationInfo.Identifier}";
            //Process.Start(new ProcessStartInfo(Totalcmd, arguments)).WaitForInputIdle();
        }
    }
}
