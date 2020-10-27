using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DokanNet;
using DokanNet.Logging;
using JetBrains.Annotations;
using FileAccess = DokanNet.FileAccess;

namespace DokanNetMirror
{
    internal partial class DokanOperations
    {
        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData | FileAccess.Execute | FileAccess.GenericExecute | FileAccess.GenericWrite | FileAccess.GenericRead;
        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData | FileAccess.Delete | FileAccess.GenericWrite;

        [NotNull]
        private readonly string _BasePath;

        [NotNull]
        private readonly ConsoleLogger _Logger = new ConsoleLogger("[DokanOperations] ");

        [NotNull]
        private readonly string _MinePath;

        public DokanOperations([NotNull] string basePath, [NotNull] string minePath) {
            _BasePath = basePath;
            _MinePath = minePath;
        }

        public void CloseFile([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
#if TRACE
            if (info.Context != null) {
                Console.WriteLine(FormatProviders.DokanFormat($"{nameof(CloseFile)}('{fileName}', {info} - entering"));
            }
#endif

            (info.Context as FileStream)?.Dispose();
            info.Context = null;
            Trace(nameof(CloseFile), fileName, info, DokanResult.Success);
            // could recreate cleanup code here but this is not called sometimes
        }

        public NtStatus FindStreams([NotNull] string fileName, [NotNull] out IList<FileInformation> streams, [NotNull] IDokanFileInfo info) {
            streams = new FileInformation[0];
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented);
        }

        public NtStatus FlushFileBuffers([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            try {
                FileStream fileStream = info.Context as FileStream;
                Debug.Assert(fileStream != null, nameof(fileStream) + " != null");
                fileStream.Flush();
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.Success);
            } catch (IOException) {
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.DiskFull);
            }
        }

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, [NotNull] IDokanFileInfo info) {
            DriveInfo dinfo = DriveInfo.GetDrives().Single(di => {
                Debug.Assert(di != null, nameof(di) + " != null");
                return string.Equals(di.RootDirectory.Name, Path.GetPathRoot(_BasePath + "\\"), StringComparison.OrdinalIgnoreCase);
            });
            Debug.Assert(dinfo != null, nameof(dinfo) + " != null");
            freeBytesAvailable = dinfo.TotalFreeSpace;
            totalNumberOfBytes = dinfo.TotalSize;
            totalNumberOfFreeBytes = dinfo.AvailableFreeSpace;
            return Trace(nameof(GetDiskFreeSpace), null, info, DokanResult.Success, "out " + freeBytesAvailable, "out " + totalNumberOfBytes, "out " + totalNumberOfFreeBytes);
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, [NotNull] IDokanFileInfo info) {
            volumeLabel = "DOKAN";
            fileSystemName = "NTFS";
            maximumComponentLength = 256;
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch | FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage | FileSystemFeatures.UnicodeOnDisk;
            return Trace(nameof(GetVolumeInformation), null, info, DokanResult.Success, "out " + volumeLabel, "out " + features, "out " + fileSystemName);
        }

        public NtStatus LockFile([NotNull] string fileName, long offset, long length, [NotNull] IDokanFileInfo info) {
            try {
                FileStream fileStream = info.Context as FileStream;
                Debug.Assert(fileStream != null, nameof(fileStream) + " != null");
                fileStream.Lock(offset, length);
                return Trace(nameof(LockFile), fileName, info, DokanResult.Success, offset, length);
            } catch (IOException) {
                return Trace(nameof(LockFile), fileName, info, DokanResult.AccessDenied, offset, length);
            }
        }

        public NtStatus Mounted([NotNull] IDokanFileInfo info) {
            return Trace(nameof(Mounted), null, info, DokanResult.Success);
        }

        public NtStatus SetAllocationSize([NotNull] string fileName, long length, [NotNull] IDokanFileInfo info) {
            try {
                FileStream fileStream = info.Context as FileStream;
                Debug.Assert(fileStream != null, nameof(fileStream) + " != null");
                fileStream.SetLength(length);
                return Trace(nameof(SetAllocationSize), fileName, info, DokanResult.Success, length);
            } catch (IOException) {
                return Trace(nameof(SetAllocationSize), fileName, info, DokanResult.DiskFull, length);
            }
        }

        public NtStatus SetEndOfFile([NotNull] string fileName, long length, [NotNull] IDokanFileInfo info) {
            try {
                FileStream fileStream = info.Context as FileStream;
                Debug.Assert(fileStream != null, nameof(fileStream) + " != null");
                fileStream.SetLength(length);
                return Trace(nameof(SetEndOfFile), fileName, info, DokanResult.Success, length);
            } catch (IOException) {
                return Trace(nameof(SetEndOfFile), fileName, info, DokanResult.DiskFull, length);
            }
        }

        public NtStatus UnlockFile([NotNull] string fileName, long offset, long length, [NotNull] IDokanFileInfo info) {
            try {
                FileStream fileStream = info.Context as FileStream;
                Debug.Assert(fileStream != null, nameof(fileStream) + " != null");
                fileStream.Unlock(offset, length);
                return Trace(nameof(UnlockFile), fileName, info, DokanResult.Success, offset, length);
            } catch (IOException) {
                return Trace(nameof(UnlockFile), fileName, info, DokanResult.AccessDenied, offset, length);
            }
        }

        public NtStatus Unmounted([NotNull] IDokanFileInfo info) {
            return Trace(nameof(Unmounted), null, info, DokanResult.Success);
        }

        [NotNull]
        private string GetBasePath([NotNull] string fileName) {
            return _BasePath + fileName;
        }

        [NotNull]
        private string GetMinePath([NotNull] string fileName) {
            return _MinePath + fileName;
        }

        private NtStatus Trace([NotNull] string method, string fileName, [NotNull] IDokanFileInfo info, NtStatus result, params object[] parameters) {
#if TRACE
            if (info.ProcessId == Program.ProcessId) {
                string extraParameters = parameters != null && parameters.Length > 0 ? ", " + string.Join(", ", parameters.Select(x => string.Format(FormatProviders.DefaultFormatProvider, "{0}", x))) : string.Empty;
                _Logger.Debug(FormatProviders.DokanFormat($"{method}('{fileName}', {info}{extraParameters}) -> {result}"));
            }
#endif

            return result;
        }

        private NtStatus Trace([NotNull] string method, string fileName, [NotNull] IDokanFileInfo info, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, NtStatus result) {
#if TRACE
            if (info.ProcessId == Program.ProcessId) {
                _Logger.Debug(FormatProviders.DokanFormat($"{method}('{fileName}', {info}, [{access}], [{share}], [{mode}], [{options}], [{attributes}]) -> {result}"));
            }
#endif

            return result;
        }
    }
}
