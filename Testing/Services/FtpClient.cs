using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Testing.Services
{
    public interface IFtpClient
    {
        [NotNull]
        Task DeleteFileAsync([NotNull] string remotePath);

        [NotNull]
        Task DownloadFileAsync([NotNull] string remotePath, [NotNull] string localPath);

        [NotNull]
        Task<DateTime?> GetDateTimestampAsync([NotNull] string remotePath);

        [NotNull]
        Task<long?> GetFileSizeAsync([NotNull] string remotePath);

        [NotNull]
        [ItemCanBeNull]
        Task<IReadOnlyCollection<FtpFileSystemInfo>> ListDirectoryAsync([NotNull] string remotePath);

        [NotNull]
        [ItemNotNull]
        Task<IReadOnlyCollection<FtpFileSystemInfo>> ListDirectoryTreeAsync([NotNull] string remotePath);

        [NotNull]
        Task MakeDirectoryAsync([NotNull] string remotePath);

        [NotNull]
        Task RemoveDirectoryAsync([NotNull] string remotePath);

        [NotNull]
        Task RenameAsync([NotNull] string remotePath, [NotNull] string renameTo);

        [NotNull]
        Task UploadFileAsync([NotNull] string remotePath, [NotNull] string localPath);
    }

    public class FtpClient : IFtpClient
    {
        [NotNull]
        private static readonly Action<FtpWebRequest> _FtpWebRequestMethodMlsd;

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

        static FtpClient() {
            // FtpWebRequest do not accept Method = "MLSD", but settings m_MethodInfo field via reflection works ...
            Type ftpMethodInfo = typeof(WebClient).Assembly.GetType("System.Net.FtpMethodInfo");
            Debug.Assert(ftpMethodInfo != null, nameof(ftpMethodInfo) + " != null");

            Type ftpOperation = typeof(WebClient).Assembly.GetType("System.Net.FtpOperation");
            Debug.Assert(ftpOperation != null, nameof(ftpOperation) + " != null");

            Type ftpMethodFlags = typeof(WebClient).Assembly.GetType("System.Net.FtpMethodFlags");
            Debug.Assert(ftpMethodFlags != null, nameof(ftpMethodFlags) + " != null");

            ConstructorInfo[] ctors = ftpMethodInfo.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            ConstructorInfo ctor = ctors.First(o => o.GetParameters().Length == 4);
            Debug.Assert(ctor != null, nameof(ctor) + " != null");

            FieldInfo field = typeof(FtpWebRequest).GetField("m_MethodInfo", BindingFlags.Instance | BindingFlags.NonPublic);
            Debug.Assert(field != null, nameof(field) + " != null");

            ParameterExpression req = Expression.Parameter(typeof(FtpWebRequest));

            // FtpMethodInfo for "LIST" has same parameters ...
            // new System.Net.FtpMethodInfo("MLSD", FtpOperation.ListDirectoryDetails, FtpMethodFlags.IsDownload | FtpMethodFlags.MayTakeParameter | FtpMethodFlags.HasHttpCommand | FtpMethodFlags.MustChangeWorkingDirectoryToPath, "GET")

            object ftpOperationValue = Enum.ToObject(ftpOperation, 2);       // ListDirectoryDetails
            object ftpMethodFlagsValue = Enum.ToObject(ftpMethodFlags, 393); // IsDownload | MayTakeParameter | HasHttpCommand | MustChangeWorkingDirectoryToPath

            NewExpression info = Expression.New(ctor, Expression.Constant("MLSD"), Expression.Constant(ftpOperationValue), Expression.Constant(ftpMethodFlagsValue), Expression.Constant("GET"));
            Expression<Action<FtpWebRequest>> lambda = Expression.Lambda<Action<FtpWebRequest>>(Expression.Assign(Expression.MakeMemberAccess(req, field), info), req);
            _FtpWebRequestMethodMlsd = lambda.Compile();
        }

        public async Task DeleteFileAsync(string remotePath) {
            FtpWebRequest request = CreateRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.DeleteFile;
            FtpWebResponse response = await GetResponseAsync(request);
            response?.Dispose();
        }

        public async Task DownloadFileAsync(string remotePath, string localPath) {
            FtpWebRequest request = CreateRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.DownloadFile;

            using (FtpWebResponse response = await GetResponseAsync(request)) {
                if (response != null) {
                    using (Stream ftpStream = response.GetResponseStream()) {
                        Debug.Assert(ftpStream != null, nameof(ftpStream) + " != null");
                        using (FileStream localStream = File.Create(localPath)) {
                            // ReSharper disable once PossibleNullReferenceException
                            await ftpStream.CopyToAsync(localStream, 2048);
                        }
                    }
                }
            }
        }

        public async Task<DateTime?> GetDateTimestampAsync(string remotePath) {
            FtpWebRequest request = CreateRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.GetDateTimestamp;
            using (FtpWebResponse response = await GetResponseAsync(request)) {
                return response != null ? response.LastModified : (DateTime?) null;
            }
        }

        public async Task<long?> GetFileSizeAsync(string remotePath) {
            FtpWebRequest request = CreateRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.GetFileSize;
            using (FtpWebResponse response = await GetResponseAsync(request)) {
                return response != null ? response.ContentLength : (long?) null;
            }
        }

        public async Task<IReadOnlyCollection<FtpFileSystemInfo>> ListDirectoryAsync(string remotePath) {
            FtpWebRequest request = CreateRequest(remotePath);
            _FtpWebRequestMethodMlsd(request);

            using (FtpWebResponse response = await GetResponseAsync(request)) {
                if (response == null) {
                    return null;
                }

                List<FtpFileSystemInfo> list = new List<FtpFileSystemInfo>();
                using (Stream stream = response.GetResponseStream()) {
                    Debug.Assert(stream != null, nameof(stream) + " != null");
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) {
                        while (!reader.EndOfStream) {
                            // ReSharper disable once PossibleNullReferenceException
                            string line = await reader.ReadLineAsync();
                            Debug.Assert(line != null, nameof(line) + " != null");

                            IEnumerable<IGrouping<string, string>> lineParserQuery =
                                from o in line.Split(';')
                                let kvp = o.Split('=')
                                group kvp.Length == 2 ? kvp[1] : o.TrimStart() by kvp.Length == 2 ? kvp[0] : string.Empty;

                            Dictionary<string, string> parts = lineParserQuery.ToDictionary(o => {
                                Debug.Assert(o != null, nameof(o) + " != null");
                                return o.Key;
                            }, o => {
                                Debug.Assert(o != null, nameof(o) + " != null");
                                return o.First();
                            });

                            string name = parts[string.Empty];
                            DateTime modify = DateTime.ParseExact(parts["modify"], "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                            Debug.Assert(name != null, nameof(name) + " != null");

                            switch (parts["type"]) {
                                case "dir":
                                    list.Add(new FtpDirectoryInfo(name, modify));
                                    break;

                                case "file":
                                    string size = parts["size"];
                                    Debug.Assert(size != null, nameof(size) + " != null");
                                    list.Add(new FtpFileInfo(name, modify, long.Parse(size)));
                                    break;
                            }
                        }
                    }
                }

                return list;
            }
        }

        public async Task<IReadOnlyCollection<FtpFileSystemInfo>> ListDirectoryTreeAsync(string remotePath) {
            List<FtpFileSystemInfo> list = new List<FtpFileSystemInfo>();

            List<FtpDirectoryInfo> dirs = new List<FtpDirectoryInfo>();
            IReadOnlyCollection<FtpFileSystemInfo> root = await ListDirectoryAsync(remotePath);
            if (root != null) {
                foreach (FtpFileSystemInfo info in root) {
                    Debug.Assert(info != null, nameof(info) + " != null");
                    info.ParentPath = remotePath + "/";
                    list.Add(info);

                    if (info is FtpDirectoryInfo directoryInfo) {
                        dirs.Add(directoryInfo);
                    }
                }
            }

            SemaphoreSlim semaphore = new SemaphoreSlim(10);

            while (dirs.Count > 0) {
                FtpDirectoryInfo[] items = dirs.ToArray();
                dirs.Clear();

                IEnumerable<Task> tasks = items.Select(async item => {
                    Debug.Assert(item != null, nameof(item) + " != null");

                    // ReSharper disable once AccessToDisposedClosure
                    await semaphore.WaitAsync();
                    try {
                        Debug.Assert(item != null, nameof(item) + " != null");

                        IReadOnlyCollection<FtpFileSystemInfo> res = await ListDirectoryAsync(item.FullName);
                        if (res != null) {
                            foreach (FtpFileSystemInfo info in res) {
                                Debug.Assert(info != null, nameof(info) + " != null");
                                info.ParentPath = item.FullName + "/";
                                list.Add(info);

                                if (info is FtpDirectoryInfo directoryInfo) {
                                    dirs.Add(directoryInfo);
                                }
                            }
                        }
                    } finally {
                        // ReSharper disable once AccessToDisposedClosure
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }

            semaphore.Dispose();

            return list.ToArray();
        }

        public async Task MakeDirectoryAsync(string remotePath) {
            FtpWebRequest request = CreateRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.MakeDirectory;
            FtpWebResponse response = await GetResponseAsync(request);
            response?.Dispose();
        }

        public async Task RemoveDirectoryAsync(string remotePath) {
            FtpWebRequest request = CreateRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.RemoveDirectory;
            FtpWebResponse response = await GetResponseAsync(request);
            response?.Dispose();
        }

        public async Task RenameAsync(string remotePath, string renameTo) {
            FtpWebRequest request = CreateRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.Rename;
            request.RenameTo = renameTo;
            FtpWebResponse response = await GetResponseAsync(request);
            response?.Dispose();
        }

        public async Task UploadFileAsync(string remotePath, string localPath) {
            FtpWebRequest request = CreateRequest(remotePath);
            request.Method = WebRequestMethods.Ftp.UploadFile;

            using (Stream ftpStream = request.GetRequestStream()) {
                Debug.Assert(ftpStream != null, nameof(ftpStream) + " != null");
                using (FileStream localStream = File.Open(localPath, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                    // ReSharper disable once PossibleNullReferenceException
                    await localStream.CopyToAsync(ftpStream, 2048);
                }
            }

            FtpWebResponse response = await GetResponseAsync(request);
            response?.Dispose();
        }

        [NotNull]
        [ItemCanBeNull]
        private static async Task<FtpWebResponse> GetResponseAsync([NotNull] FtpWebRequest request) {
            try {
                return (FtpWebResponse) await request.GetResponseAsync();
            } catch (WebException exc) {
                Console.WriteLine(exc);
                Debug.Assert(exc.Response != null, "exc.Response != null");
                exc.Response.Dispose();
                return null;
            }
        }

        [NotNull]
        private FtpWebRequest CreateRequest([NotNull] string remotePath) {
            Debug.Assert(remotePath.Length > 0 && remotePath[0] == '/', "remotePath[0] == '/'");
            FtpWebRequest request = (FtpWebRequest) WebRequest.Create("ftp://" + _Host + remotePath);
            request.Credentials = new NetworkCredential(_UserName, _Password);
            request.UseBinary = true;
            request.UsePassive = true;
            return request;
        }
    }

    [DebuggerDisplay("D|{FullName} - {Modify}")]
    public class FtpDirectoryInfo : FtpFileSystemInfo
    {
        public FtpDirectoryInfo([NotNull] string name, DateTime modify) : base(name, modify) {
        }
    }

    [DebuggerDisplay("F|{FullName} - {SizeString,nq}; {Modify}")]
    public class FtpFileInfo : FtpFileSystemInfo
    {
        private const string SizePrefixes = " kMGT";

        public FtpFileInfo([NotNull] string name, DateTime modify, long size)
            : base(name, modify) {
            Size = size;
        }

        public long Size { get; }

        [NotNull]
        private string SizeString {
            get {
                decimal value = Size;
                int index = 0;
                while (value > 1280 && index < SizePrefixes.Length - 1) {
                    value /= 1024m;
                    ++index;
                }

                return value.ToString("0.##", CultureInfo.InvariantCulture) + SizePrefixes[index];
            }
        }
    }

    public abstract class FtpFileSystemInfo
    {
        [NotNull]
        private string _ParentPath;

        protected FtpFileSystemInfo([NotNull] string name, DateTime modify) {
            Name = name;
            Modify = modify;
            _ParentPath = "/";
        }

        [NotNull]
        public string FullName {
            get { return ParentPath + Name; }
        }

        public DateTime Modify { get; }

        [NotNull]
        public string Name { get; }

        [NotNull]
        public string ParentPath {
            get { return _ParentPath; }
            set {
                if (value != null && !value.EndsWith("/")) {
                    throw new InvalidDataException("ParentPath must end with '/'");
                }

                _ParentPath = value;
            }
        }
    }
}
