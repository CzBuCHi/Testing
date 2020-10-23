using System;
using DokanNet;

namespace DokanNetMirror
{
    internal class Program
    {
        private static void Main() {
            try {
                string mirrorPath = @"c:\projects\Testing\test\mirror";
                string mountPath = @"c:\projects\Testing\test\mount";

                Notify.Start(mirrorPath, mountPath);

                Mirror mirror = new Mirror(mirrorPath);
                mirror.Mount(mountPath, DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI, 5);

                Console.WriteLine(@"Success");
            } catch (DokanException ex) {
                Console.WriteLine(@"Error: " + ex.Message);
            }
        }
    }
}
