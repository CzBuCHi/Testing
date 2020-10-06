using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using DokanNet;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;
using FileAccess = DokanNet.FileAccess;

namespace Testing.Services
{
    public class DokanOperations : IDokanOperations
    {
        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData | FileAccess.Execute | FileAccess.GenericExecute | FileAccess.GenericWrite | FileAccess.GenericRead;
        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData | FileAccess.Delete | FileAccess.GenericWrite;

        private static readonly IEqualityComparer<FileSystemInfo> _FileSystemInfoComparer = new FileSystemInfoComparer();

        [NotNull]
        private readonly string _BasePath;

        [NotNull]
        private readonly string _MinePath;

        public DokanOperations([NotNull] string basePath, [NotNull] string minePath) {
            _BasePath = basePath;
            _MinePath = minePath;
        }

        public void Cleanup([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            (info.Context as FileStream)?.Dispose();
            info.Context = null;

            if (info.DeleteOnClose) {
                if (info.IsDirectory) {
                    Directory.Delete(GetBasePath(fileName));
                } else {
                    File.Delete(GetBasePath(fileName));
                }
            }
        }

        public void CloseFile([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            (info.Context as FileStream)?.Dispose();
            info.Context = null;

            // could recreate cleanup code here but this is not called sometimes
        }

        public NtStatus CreateFile([NotNull] string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, [NotNull] IDokanFileInfo info) {
            return info.IsDirectory
                ? CreateFile_Directory(fileName, mode)
                : CreateFile_File(fileName, access, share, mode, options, attributes, info);
        }

        public NtStatus DeleteDirectory([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            return Directory.EnumerateFileSystemEntries(GetBasePath(fileName)).Any() ? DokanResult.DirectoryNotEmpty : DokanResult.Success;
            // if dir is not empty it can't be deleted
        }

        public NtStatus DeleteFile([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            string filePath = GetBasePath(fileName);

            if (Directory.Exists(filePath)) {
                return DokanResult.AccessDenied;
            }

            if (!File.Exists(filePath)) {
                return DokanResult.FileNotFound;
            }

            if (File.GetAttributes(filePath).HasFlag(FileAttributes.Directory)) {
                return DokanResult.AccessDenied;
            }

            return DokanResult.Success;
            // we just check here if we could delete the file - the true deletion is in Cleanup
        }

        public NtStatus FindFiles([NotNull] string fileName, [NotNull] out IList<FileInformation> files, [NotNull] IDokanFileInfo info) {
            // This function is not called because FindFilesWithPattern is implemented
            // Return DokanResult.NotImplemented in FindFilesWithPattern to make FindFiles called
            return FindFilesWithPattern(fileName, "*", out files, info);
        }

        public NtStatus FindFilesWithPattern([NotNull] string fileName, string searchPattern, out IList<FileInformation> files, [NotNull] IDokanFileInfo info) {
            bool Predicate(FileSystemInfo finfo) {
                return finfo != null && DokanHelper.DokanIsNameInExpression(searchPattern, finfo.Name, true);
            }

            IEnumerable<FileSystemInfo> mineQuery = new DirectoryInfo(GetMinePath(fileName)).EnumerateFileSystemInfos().Where(Predicate);
            IEnumerable<FileSystemInfo> baseQuery = new DirectoryInfo(GetBasePath(fileName)).EnumerateFileSystemInfos().Where(Predicate);

            files = mineQuery
                    // grab all form mine & merge with all from base that arent in mine
                   .Union(baseQuery, _FileSystemInfoComparer)
                   .Select(o => {
                        Debug.Assert(o != null, nameof(o) + " != null");
                        return new FileInformation {
                            Attributes = o.Attributes,
                            CreationTime = o.CreationTime,
                            LastAccessTime = o.LastAccessTime,
                            LastWriteTime = o.LastWriteTime,
                            Length = (o as FileInfo)?.Length ?? 0,
                            FileName = o.Name
                        };
                    })
                   .ToArray();

            return DokanResult.Success;
        }

        public NtStatus FindStreams([NotNull] string fileName, [NotNull] out IList<FileInformation> streams, [NotNull] IDokanFileInfo info) {
            streams = new FileInformation[0];
            return DokanResult.NotImplemented;
        }

        public NtStatus FlushFileBuffers([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).Flush();
                return DokanResult.Success;
            } catch (IOException) {
                return DokanResult.DiskFull;
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
            return DokanResult.Success;
        }

        public NtStatus GetFileInformation([NotNull] string fileName, out FileInformation fileInfo, [NotNull] IDokanFileInfo info) {
            // may be called with info.Context == null, but usually it isn't

            FileSystemInfo fsi = null;
            if (fileName != "\\") {
                string minePath = GetMinePath(fileName);
                fsi = new FileInfo(minePath);
                if (!fsi.Exists) {
                    fsi = new DirectoryInfo(minePath);
                    if (!fsi.Exists) {
                        fsi = null;
                    }
                }
            }

            if (fsi == null) {
                string basePath = GetBasePath(fileName);
                fsi = new FileInfo(basePath);
                if (!fsi.Exists) {
                    fsi = new DirectoryInfo(basePath);
                }
            }


            fileInfo = new FileInformation {
                FileName = fileName,
                Attributes = fsi.Attributes,
                CreationTime = fsi.CreationTime,
                LastAccessTime = fsi.LastAccessTime,
                LastWriteTime = fsi.LastWriteTime,
                Length = (fsi as FileInfo)?.Length ?? 0
            };
            return DokanResult.Success;
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
            return DokanResult.Success;
        }

        public NtStatus LockFile([NotNull] string fileName, long offset, long length, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).Lock(offset, length);
                return DokanResult.Success;
            } catch (IOException) {
                return DokanResult.AccessDenied;
            }
        }

        public NtStatus Mounted([NotNull] IDokanFileInfo info) {
            return DokanResult.Success;
        }

        public NtStatus MoveFile([NotNull] string oldName, [NotNull] string newName, bool replace, [NotNull] IDokanFileInfo info) {
            string oldpath = GetBasePath(oldName);
            string newpath = GetBasePath(newName);

            (info.Context as FileStream)?.Dispose();
            info.Context = null;

            bool exist = info.IsDirectory ? Directory.Exists(newpath) : File.Exists(newpath);

            try {
                if (!exist) {
                    info.Context = null;
                    if (info.IsDirectory) {
                        Directory.Move(oldpath, newpath);
                    } else {
                        File.Move(oldpath, newpath);
                    }

                    return DokanResult.Success;
                }

                if (replace) {
                    info.Context = null;

                    //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
                    if (info.IsDirectory) {
                        return DokanResult.AccessDenied;
                    }

                    File.Delete(newpath);
                    File.Move(oldpath, newpath);
                    return DokanResult.Success;
                }
            } catch (UnauthorizedAccessException) {
                return DokanResult.AccessDenied;
            }

            return DokanResult.FileExists;
        }

        public NtStatus ReadFile([NotNull] string fileName, [NotNull] byte[] buffer, out int bytesRead, long offset, [NotNull] IDokanFileInfo info) {
            Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
            FileStream contextStream = (FileStream) info.Context;

            // Protect from overlapped read
            lock (contextStream) {
                contextStream.Position = offset;
                bytesRead = contextStream.Read(buffer, 0, buffer.Length);
            }

            return DokanResult.Success;
        }

        public NtStatus SetAllocationSize([NotNull] string fileName, long length, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).SetLength(length);
                return DokanResult.Success;
            } catch (IOException) {
                return DokanResult.DiskFull;
            }
        }

        public NtStatus SetEndOfFile([NotNull] string fileName, long length, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).SetLength(length);
                return DokanResult.Success;
            } catch (IOException) {
                return DokanResult.DiskFull;
            }
        }

        public NtStatus SetFileAttributes([NotNull] string fileName, FileAttributes attributes, [NotNull] IDokanFileInfo info) {
            try {
                // MS-FSCC 2.6 File Attributes : There is no file attribute with the value 0x00000000
                // because a value of 0x00000000 in the FileAttributes field means that the file attributes for this file MUST NOT be changed when setting basic information for the file
                if (attributes != 0) {
                    File.SetAttributes(GetBasePath(fileName), attributes);
                }

                return DokanResult.Success;
            } catch (UnauthorizedAccessException) {
                return DokanResult.AccessDenied;
            } catch (FileNotFoundException) {
                return DokanResult.FileNotFound;
            } catch (DirectoryNotFoundException) {
                return DokanResult.PathNotFound;
            }
        }

        public NtStatus SetFileSecurity([NotNull] string fileName, FileSystemSecurity security, AccessControlSections sections, [NotNull] IDokanFileInfo info) {
            return DokanResult.NotImplemented;
        }

        public NtStatus SetFileTime([NotNull] string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, [NotNull] IDokanFileInfo info) {
            try {
                if (info.Context is FileStream stream) {
                    long ct = creationTime?.ToFileTime() ?? 0;
                    long lat = lastAccessTime?.ToFileTime() ?? 0;
                    long lwt = lastWriteTime?.ToFileTime() ?? 0;
                    if (NativeMethods.SetFileTime(stream.SafeFileHandle, ref ct, ref lat, ref lwt)) {
                        return DokanResult.Success;
                    }

                    throw Marshal.GetExceptionForHR(Marshal.GetLastWin32Error()) ?? new Exception("Unknown exception during 'SetFileTime' call.");
                }

                string filePath = GetBasePath(fileName);

                if (creationTime.HasValue) {
                    File.SetCreationTime(filePath, creationTime.Value);
                }

                if (lastAccessTime.HasValue) {
                    File.SetLastAccessTime(filePath, lastAccessTime.Value);
                }

                if (lastWriteTime.HasValue) {
                    File.SetLastWriteTime(filePath, lastWriteTime.Value);
                }

                return DokanResult.Success;
            } catch (UnauthorizedAccessException) {
                return DokanResult.AccessDenied;
            } catch (FileNotFoundException) {
                return DokanResult.FileNotFound;
            }
        }

        public NtStatus UnlockFile([NotNull] string fileName, long offset, long length, [NotNull] IDokanFileInfo info) {
            try {
                Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
                ((FileStream) info.Context).Unlock(offset, length);
                return DokanResult.Success;
            } catch (IOException) {
                return DokanResult.AccessDenied;
            }
        }

        public NtStatus Unmounted([NotNull] IDokanFileInfo info) {
            return DokanResult.Success;
        }

        public NtStatus WriteFile([NotNull] string fileName, [NotNull] byte[] buffer, out int bytesWritten, long offset, [NotNull] IDokanFileInfo info) {
            Debug.Assert(info.Context is FileStream, "info.Context is FileStream");
            FileStream contentStream = (FileStream) info.Context;

            // Protect from overlapped write
            lock (contentStream) {
                contentStream.Position = offset;
                contentStream.Write(buffer, 0, buffer.Length);
            }

            bytesWritten = buffer.Length;

            return DokanResult.Success;
        }

        [NotNull]
        protected string GetBasePath([NotNull] string fileName) {
            return _BasePath + fileName;
        }

        [NotNull]
        protected string GetMinePath([NotNull] string fileName) {
            if (fileName == null) {
                throw new ArgumentNullException(nameof(fileName));
            }

            if (fileName == null) {
                throw new ArgumentNullException(nameof(fileName));
            }

            return _MinePath + fileName;
        }

        private NtStatus CreateFile_Directory([NotNull] string fileName, FileMode mode) {
            try {
                switch (mode) {
                    case FileMode.Open: {
                        string filePath = GetBasePath(fileName);
                        if (!Directory.Exists(filePath)) {
                            try {
                                if (!File.GetAttributes(filePath).HasFlag(FileAttributes.Directory)) {
                                    return DokanResult.NotADirectory;
                                }
                            } catch (Exception) {
                                return DokanResult.FileNotFound;
                            }

                            return DokanResult.PathNotFound;
                        }

                        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                        new DirectoryInfo(filePath).EnumerateFileSystemInfos().Any();
                        // you can't list the directory
                        break;
                    }
                    case FileMode.CreateNew: {
                        string filePath = GetBasePath(fileName);
                        if (Directory.Exists(filePath)) {
                            return DokanResult.FileExists;
                        }

                        try {
                            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                            File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
                            return DokanResult.AlreadyExists;
                        } catch (IOException) {
                        }

                        Directory.CreateDirectory(GetBasePath(fileName));
                        break;
                    }
                }
            } catch (UnauthorizedAccessException) {
                return DokanResult.AccessDenied;
            }

            return DokanResult.Success;
        }

        private NtStatus CreateFile_File([NotNull] string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, [NotNull] IDokanFileInfo info) {
            NtStatus result = DokanResult.Success;

            bool pathExists = true;
            bool pathIsDirectory = false;

            bool readWriteAttributes = (access & DataAccess) == 0;
            bool readAccess = (access & DataWriteAccess) == 0;

            string filePath = null;

            try {
                switch (mode) {
                    case FileMode.Open:
                        filePath = GetMinePath(fileName);
                        if (!Directory.Exists(filePath) && !File.Exists(filePath)) {
                            filePath = GetBasePath(fileName);
                        }

                        break;

                    case FileMode.OpenOrCreate:
                        filePath = GetBasePath(fileName);
                        if (!Directory.Exists(filePath) && !File.Exists(filePath)) {
                            filePath = GetMinePath(fileName);
                        }

                        break;

                    case FileMode.Create:
                        filePath = GetMinePath(fileName);
                        break;

                    default:
                        Debugger.Break();
                        filePath = GetBasePath(fileName);
                        break;
                }

                pathExists = Directory.Exists(filePath) || File.Exists(filePath);
                pathIsDirectory = pathExists && File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
            } catch (IOException) {
            }

            switch (mode) {
                case FileMode.Open:

                    if (pathExists) {
                        // check if driver only wants to read attributes, security info, or open directory
                        if (readWriteAttributes || pathIsDirectory) {
                            if (pathIsDirectory && (access & FileAccess.Delete) == FileAccess.Delete && (access & FileAccess.Synchronize) != FileAccess.Synchronize) {
                                //It is a DeleteFile request on a directory
                                return DokanResult.AccessDenied;
                            }

                            info.IsDirectory = pathIsDirectory;
                            info.Context = new object();
                            // must set it to someting if you return DokanError.Success

                            return DokanResult.Success;
                        }
                    } else {
                        return DokanResult.FileNotFound;
                    }

                    break;

                case FileMode.CreateNew:
                    if (pathExists) {
                        return DokanResult.FileExists;
                    }

                    break;

                case FileMode.Truncate:
                    if (!pathExists) {
                        return DokanResult.FileNotFound;
                    }

                    break;
            }

            try {
                Debug.Assert(filePath != null, nameof(filePath) + " != null");
                info.Context = new FileStream(filePath, mode, readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite, share, 4096, options);

                if (pathExists && (mode == FileMode.OpenOrCreate || mode == FileMode.Create)) {
                    result = DokanResult.AlreadyExists;
                }

                bool fileCreated = mode == FileMode.CreateNew || mode == FileMode.Create || !pathExists && mode == FileMode.OpenOrCreate;
                if (fileCreated) {
                    FileAttributes newAttributes = attributes;
                    newAttributes |= FileAttributes.Archive; // Files are always created as Archive
                    // FILE_ATTRIBUTE_NORMAL is override if any other attribute is set.
                    newAttributes &= ~FileAttributes.Normal;
                    File.SetAttributes(filePath, newAttributes);
                }
            } catch (UnauthorizedAccessException) {
                // don't have access rights
                if (info.Context is FileStream fileStream) {
                    // returning AccessDenied cleanup and close won't be called,
                    // so we have to take care of the stream now
                    fileStream.Dispose();
                    info.Context = null;
                }

                return DokanResult.AccessDenied;
            } catch (DirectoryNotFoundException) {
                return DokanResult.PathNotFound;
            } catch (Exception ex) {
                uint hr = (uint) Marshal.GetHRForException(ex);
                switch (hr) {
                    case 0x80070020: //Sharing violation
                        return DokanResult.SharingViolation;
                    default:
                        throw;
                }
            }

            return result;
        }

        private static class NativeMethods
        {
            /// <summary>
            ///     Sets the date and time that the specified file or directory was created, last accessed, or last modified.
            /// </summary>
            /// <param name="hFile">
            ///     A <see cref="SafeFileHandle" /> to the file or directory.
            ///     To get the handler, <see cref="System.IO.FileStream.SafeFileHandle" /> can be used.
            /// </param>
            /// <param name="lpCreationTime">
            ///     A Windows File Time that contains the new creation date and time
            ///     for the file or directory.
            ///     If the application does not need to change this information, set this parameter to 0.
            /// </param>
            /// <param name="lpLastAccessTime">
            ///     A Windows File Time that contains the new last access date and time
            ///     for the file or directory. The last access time includes the last time the file or directory
            ///     was written to, read from, or (in the case of executable files) run.
            ///     If the application does not need to change this information, set this parameter to 0.
            /// </param>
            /// <param name="lpLastWriteTime">
            ///     A Windows File Time that contains the new last modified date and time
            ///     for the file or directory. If the application does not need to change this information,
            ///     set this parameter to 0.
            /// </param>
            /// <returns>If the function succeeds, the return value is <c>true</c>.</returns>
            /// \see
            /// <a href="https://msdn.microsoft.com/en-us/library/windows/desktop/ms724933">SetFileTime function (MSDN)</a>
            [DllImport("kernel32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool SetFileTime(SafeFileHandle hFile, ref long lpCreationTime, ref long lpLastAccessTime, ref long lpLastWriteTime);
        }

        private class FileSystemInfoComparer : IEqualityComparer<FileSystemInfo>
        {
            public bool Equals(FileSystemInfo x, FileSystemInfo y) {
                return x != null && y != null && x.Name == y.Name;
            }

            public int GetHashCode(FileSystemInfo obj) {
                return obj.Name.GetHashCode();
            }
        }
    }
}
