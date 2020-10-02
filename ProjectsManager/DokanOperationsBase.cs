using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using DokanNet;
using DokanNet.Logging;
using JetBrains.Annotations;
using FileAccess = DokanNet.FileAccess;

namespace ProjectsManager
{
    public abstract class DokanOperationsBase
    {
        [NotNull]
        private readonly ConsoleLogger _Logger = new ConsoleLogger("[Mirror] ");

        public void CloseFile([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            (info.Context as FileStream)?.Dispose();
            info.Context = null;
            Trace(nameof(CloseFile), fileName, info, DokanResult.Success);
            // could recreate cleanup code here but this is not called sometimes
        }

        public NtStatus CreateFile([NotNull] string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, [NotNull] IDokanFileInfo info) {
            NtStatus result = info.IsDirectory
                ? CreateFile_Directory(fileName, access, share, mode, options, attributes, info)
                : CreateFile_File(fileName, access, share, mode, options, attributes, info);
            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, result);
        }

        public NtStatus FindFiles([NotNull] string fileName, [NotNull] out IList<FileInformation> files, [NotNull] IDokanFileInfo info) {
            // This function is not called because FindFilesWithPattern is implemented
            // Return DokanResult.NotImplemented in FindFilesWithPattern to make FindFiles called
            return FindFilesWithPattern(fileName, "*", out files, info);
        }

        public abstract NtStatus FindFilesWithPattern([NotNull] string fileName, string searchPattern, [NotNull] out IList<FileInformation> files, [NotNull] IDokanFileInfo info);

        public NtStatus FindStreams([NotNull] string fileName, [NotNull] out IList<FileInformation> streams, [NotNull] IDokanFileInfo info) {
            streams = new FileInformation[0];
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented);
        }

        public NtStatus FlushFileBuffers([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).Flush();
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.Success);
            } catch (IOException) {
                return Trace(nameof(FlushFileBuffers), fileName, info, DokanResult.DiskFull);
            }
        }

        public NtStatus GetFileSecurity([NotNull] string fileName, out FileSystemSecurity security, AccessControlSections sections, [NotNull] IDokanFileInfo info) {
            security = null;
            return DokanResult.NotImplemented;
        }

        public NtStatus GetVolumeInformation([NotNull] out string volumeLabel, out FileSystemFeatures features, [NotNull] out string fileSystemName, out uint maximumComponentLength, [NotNull] IDokanFileInfo info) {
            volumeLabel = "DOKAN";
            fileSystemName = "NTFS";
            maximumComponentLength = 256;
            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch | FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage | FileSystemFeatures.UnicodeOnDisk;
            return Trace(nameof(GetVolumeInformation), null, info, DokanResult.Success, "out " + volumeLabel, "out " + features, "out " + fileSystemName);
        }

        public NtStatus LockFile([NotNull] string fileName, long offset, long length, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).Lock(offset, length);
                return Trace(nameof(LockFile), fileName, info, DokanResult.Success, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            } catch (IOException) {
                return Trace(nameof(LockFile), fileName, info, DokanResult.AccessDenied, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus Mounted([NotNull] IDokanFileInfo info) {
            return Trace(nameof(Mounted), null, info, DokanResult.Success);
        }

        public NtStatus SetAllocationSize([NotNull] string fileName, long length, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).SetLength(length);
                return Trace(nameof(SetAllocationSize), fileName, info, DokanResult.Success, length.ToString(CultureInfo.InvariantCulture));
            } catch (IOException) {
                return Trace(nameof(SetAllocationSize), fileName, info, DokanResult.DiskFull, length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus SetEndOfFile([NotNull] string fileName, long length, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).SetLength(length);
                return Trace(nameof(SetEndOfFile), fileName, info, DokanResult.Success, length.ToString(CultureInfo.InvariantCulture));
            } catch (IOException) {
                return Trace(nameof(SetEndOfFile), fileName, info, DokanResult.DiskFull, length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus SetFileSecurity([NotNull] string fileName, FileSystemSecurity security, AccessControlSections sections, [NotNull] IDokanFileInfo info) {
            return DokanResult.NotImplemented;
        }

        public NtStatus UnlockFile([NotNull] string fileName, long offset, long length, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).Unlock(offset, length);
                return Trace(nameof(UnlockFile), fileName, info, DokanResult.Success, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            } catch (IOException) {
                return Trace(nameof(UnlockFile), fileName, info, DokanResult.AccessDenied, offset.ToString(CultureInfo.InvariantCulture), length.ToString(CultureInfo.InvariantCulture));
            }
        }

        public NtStatus Unmounted([NotNull] IDokanFileInfo info) {
            return Trace(nameof(Unmounted), null, info, DokanResult.Success);
        }

        protected abstract NtStatus CreateFile_Directory([NotNull] string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, [NotNull] IDokanFileInfo info);
        protected abstract NtStatus CreateFile_File([NotNull] string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, [NotNull] IDokanFileInfo info);

        protected NtStatus Trace(string method, string fileName, IDokanFileInfo info, NtStatus result, params object[] parameters) {
#if TRACE
            string extraParameters = parameters != null && parameters.Length > 0 ? ", " + string.Join(", ", parameters.Select(x => string.Format(FormatProviders.DefaultFormatProvider, "{0}", x))) : string.Empty;
            _Logger.Debug(FormatProviders.DokanFormat($"{method}('{fileName}', {info}{extraParameters}) -> {result}"));
#endif
            return result;
        }

        protected NtStatus Trace(string method, string fileName, IDokanFileInfo info, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, NtStatus result) {
#if TRACE
            _Logger.Debug(FormatProviders.DokanFormat($"{method}('{fileName}', {info}, [{access}], [{share}], [{mode}], [{options}], [{attributes}]) -> {result}"));
#endif
            return result;
        }
    }
}
