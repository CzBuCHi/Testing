using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;

namespace ProjectsManager
{
    public class FtpClient
    {
        private const int BufferSize = 2048;

        [NotNull]
        private readonly string _Host;

        [NotNull]
        private readonly string _Password;

        [NotNull]
        private readonly string _UserName;

        public FtpClient([NotNull] string host, [NotNull] string userName, [NotNull] string password) {
            _Host = host;
            _UserName = userName;
            _Password = password;
        }

        public void DownloadFile([NotNull] string remotePath, [NotNull] string localPath) {
            FtpWebRequest request = CreateFtpWebRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.DownloadFile;

            using (FtpWebResponse ftpResponse = GetResponse(request)) {
                using (Stream ftpStream = ftpResponse.GetResponseStream()) {
                    Debug.Assert(ftpStream != null, nameof(ftpStream) + " != null");
                    using (FileStream localFileStream = new FileStream(localPath, FileMode.Create)) {
                        byte[] byteBuffer = new byte[BufferSize];
                        int bytesRead = ftpStream.Read(byteBuffer, 0, BufferSize);
                        while (bytesRead > 0) {
                            localFileStream.Write(byteBuffer, 0, bytesRead);
                            bytesRead = ftpStream.Read(byteBuffer, 0, BufferSize);
                        }
                    }
                }
            }
        }

        public DateTime GetDateTimestamp([NotNull] string remotePath) {
            FtpWebRequest request = CreateFtpWebRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.GetDateTimestamp;
            FtpWebResponse response = GetResponse(request);
            return response.LastModified;
        }

        [NotNull]
        [ItemNotNull]
        public IEnumerable<FtpFileSystemInfo> ListDirectory([NotNull] string remotePath) {
            FtpWebRequest request = CreateFtpWebRequest(remotePath);
            FtpWebRequestSetMlsd(request);

            using (FtpWebResponse response = GetResponse(request)) {
                using (Stream stream = response.GetResponseStream()) {
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) {
                        while (!reader.EndOfStream) {
                            string line = reader.ReadLine();
                            Dictionary<string, string> data = new Dictionary<string, string>();
                            foreach (string part in line.Split(';')) {
                                string[] kvp = part.Split('=');
                                if (kvp.Length == 2) {
                                    data.Add(kvp[0], kvp[1]);
                                } else {
                                    data.Add("", part.TrimStart());
                                }
                            }

                            switch (data["type"]) {
                                case "dir":
                                case "file":
                                    DateTime modify = DateTime.ParseExact(data["modify"], "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                                    string name = data[""];
                                    Debug.Assert(name != null, nameof(name) + " != null");

                                    if (data["type"] == "dir") {
                                        
                                        yield return new FtpDirectoryInfo(name, modify);
                                    } else {
                                        ulong size = ulong.Parse(data["size"]);
                                        yield return new FtpFileInfo(name, modify, size);
                                    }

                                    break;
                            }
                        }
                    }
                }
            }
        }

        public void UploadFile([NotNull] string remotePath, [NotNull] string localPath) {
            FtpWebRequest request = CreateFtpWebRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.UploadFile;

            using (Stream ftpStream = request.GetRequestStream()) {
                using (FileStream localFileStream = new FileStream(localPath, FileMode.Create)) {
                    byte[] byteBuffer = new byte[BufferSize];
                    int bytesSent = localFileStream.Read(byteBuffer, 0, BufferSize);
                    while (bytesSent != 0) {
                        ftpStream.Write(byteBuffer, 0, bytesSent);
                        bytesSent = localFileStream.Read(byteBuffer, 0, BufferSize);
                    }
                }
            }

            GetResponse(request);
        }

        private static void FtpWebRequestSetMlsd([NotNull] FtpWebRequest request) {
            // FtpWebRequest do not support "MLSD" command out of the box, but setting it via reflection works ...
            // if supported this would do same thing as: 'request.Method = "MLSD";'
            Type type = typeof(WebClient).Assembly.GetType("System.Net.FtpMethodInfo");
            Debug.Assert(type != null, nameof(type) + " != null");
            ConstructorInfo[] ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            ConstructorInfo ctor = ctors.First(o => o.GetParameters().Length == 4);
            Debug.Assert(ctor != null, nameof(ctor) + " != null");
            FieldInfo field = typeof(FtpWebRequest).GetField("m_MethodInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            Debug.Assert(field != null, nameof(field) + " != null");
            field.SetValue(request, ctor.Invoke(new object[] { "MLSD", 2, 393, "GET" })); // params same as "LIST" command
        }

        [NotNull]
        private static FtpWebResponse GetResponse([NotNull] FtpWebRequest request) {
            try {
                return (FtpWebResponse) request.GetResponse();
            } catch (WebException exc) {
                return (FtpWebResponse) exc.Response;
            }
        }

        [NotNull]
        private FtpWebRequest CreateFtpWebRequest([NotNull] string remotePath) {
            string uri = "ftp://" + _Host;
            if (!string.IsNullOrEmpty(remotePath)) {
                uri += remotePath;
            }

            FtpWebRequest ftpRequest = (FtpWebRequest) WebRequest.Create(uri);
            ftpRequest.Credentials = new NetworkCredential(_UserName, _Password);
            ftpRequest.UseBinary = true;
            ftpRequest.UsePassive = true;
            return ftpRequest;
        }
    }

    [DebuggerDisplay("DIR: {FullPath} [{Modify}]")]
    public class FtpDirectoryInfo : FtpFileSystemInfo
    {
        public FtpDirectoryInfo([NotNull] string name, DateTime modify) : base(name, modify) {
        }
    }

    [DebuggerDisplay("FILE: {FullPath} [{Modify}; {SizeString,nq}]")]
    public class FtpFileInfo : FtpFileSystemInfo
    {
        public FtpFileInfo([NotNull] string name, DateTime modify, ulong size) : base(name, modify) {
            Size = size;
        }

        public ulong Size { get; }

        public string SizeString {
            get {
                string[] suffixes = { "", "k", "M", "G", "T" };
                decimal value = Size;
                int index = 0;
                while (value > 1280 || index == suffixes.Length - 1) {
                    value /= 1024;
                    ++index;
                }

                return value.ToString("0.##", CultureInfo.InvariantCulture) + suffixes[index];
            }
        }
    }

    public abstract class FtpFileSystemInfo
    {
        protected FtpFileSystemInfo([NotNull] string name, DateTime modify) {
            Name = name;
            Modify = modify;
        }

        public string FullPath {
            get { return string.IsNullOrEmpty(ParentPath) ? "/" + Name : ParentPath + "/" + Name; }
        }

        public DateTime Modify { get; }

        [NotNull]
        public string Name { get; }

        [CanBeNull]
        public string ParentPath { get; set; }
    }
}
