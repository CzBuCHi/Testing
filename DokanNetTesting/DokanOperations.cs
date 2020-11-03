using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading.Tasks;
using DokanNet;
using JetBrains.Annotations;
using FileAccess = DokanNet.FileAccess;

namespace DokanNetTesting
{
    public delegate void DokanOperationsDelegate(string fileName);

    public class DokanOperations : IDokanOperations
    {
        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData | FileAccess.Execute | FileAccess.GenericExecute | FileAccess.GenericWrite | FileAccess.GenericRead;
        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData | FileAccess.Delete | FileAccess.GenericWrite;

        [NotNull]
        private readonly string _MinePath;



        public DokanOperations([NotNull] string minePath) {
            _MinePath = minePath;

        }

        public void Cleanup([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            (info.Context as FileStream)?.Dispose();
            info.Context = null;

            if (info.DeleteOnClose) {
                if (info.IsDirectory) {
                    Directory.Delete(GetMinePath(fileName));
                } else {
                    File.Delete(GetMinePath(fileName));
                }
            }

            if (_WriteFile.Contains(fileName)) {
                _WriteFile.Remove(fileName);
                Task.Factory.StartNew(() => {
                    OnAfterWriteFile(fileName);
                });
            }
        }

        public void CloseFile(string fileName, [NotNull] IDokanFileInfo info) {
            (info.Context as FileStream)?.Dispose();
            info.Context = null;
            // could recreate cleanup code here but this is not called sometimes
        }

        [NotNull]
        private readonly HashSet<string> _WriteFile = new HashSet<string>();

        public event DokanOperationsDelegate BeforeWriteFile;

        private void OnBeforeWriteFile(string filename) { 
            BeforeWriteFile?.Invoke(filename); 
        }

        public event DokanOperationsDelegate BeforeReadFile;

        private void OnBeforeReadFile(string filename) {
            BeforeReadFile?.Invoke(filename);
        }

        public event DokanOperationsDelegate AfterWriteFile;

        private void OnAfterWriteFile(string filename) { 
            AfterWriteFile?.Invoke(filename); 
        }

        public NtStatus CreateFile([NotNull] string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, [NotNull] IDokanFileInfo info) {
            NtStatus result = DokanResult.Success;
            string filePath = GetMinePath(fileName);

            if (info.IsDirectory) {
                try {
                    switch (mode) {
                        case FileMode.Open: {
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
                            if (Directory.Exists(filePath)) {
                                return DokanResult.FileExists;
                            }

                            try {
                                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                                File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
                                return DokanResult.AlreadyExists;
                            } catch (IOException) {
                            }

                            Directory.CreateDirectory(GetMinePath(fileName));
                            break;
                        }
                    }
                } catch (UnauthorizedAccessException) {
                    return DokanResult.AccessDenied;
                }
            } else {
                bool pathExists = true;
                bool pathIsDirectory = false;

                bool readWriteAttributes = (access & DataAccess) == 0;
                bool readAccess = (access & DataWriteAccess) == 0;

                try {
                    pathExists = Directory.Exists(filePath) || File.Exists(filePath);
                    pathIsDirectory = pathExists && File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
                } catch (IOException) {
                }

                switch (mode) {
                    case FileMode.Open: {

                        if (pathExists) {
                            // check if driver only wants to read attributes, security info, or open directory
                            if (readWriteAttributes || pathIsDirectory) {
                                if (pathIsDirectory && access.HasFlag(FileAccess.Delete) && !access.HasFlag(FileAccess.Synchronize)) {
                                    // It is a DeleteFile request on a directory
                                    return DokanResult.AccessDenied;
                                }

                                info.IsDirectory = pathIsDirectory;
                                info.Context = new object();
                                // must set it to someting if you return DokanError.Success

                                OnBeforeReadFile(fileName);

                                return DokanResult.Success;
                            }
                        } else {
                            return DokanResult.FileNotFound;
                        }

                        break;
                    }

                    case FileMode.CreateNew: {
                        if (pathExists) {
                            return DokanResult.FileExists;
                        }

                        break;
                    }

                    case FileMode.Truncate: {
                        if (!pathExists) {
                            return DokanResult.FileNotFound;
                        }

                        break;
                    }
                }

                try {
                    _WriteFile.Add(fileName);
                    OnBeforeWriteFile(fileName);

                    if (pathExists && (mode == FileMode.OpenOrCreate || mode == FileMode.Create)) {
                        result = DokanResult.AlreadyExists;
                    }
                    
                    info.Context = new FileStream(filePath, mode, readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite, share, 4096, options);

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
                        case 0x80070020:
                            return DokanResult.SharingViolation;
                        default:
                            throw;
                    }
                }
            }

            return result;
        }

        public NtStatus DeleteDirectory([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            // if dir is not empty it can't be deleted
            return Directory.EnumerateFileSystemEntries(GetMinePath(fileName)).Any()
                ? DokanResult.DirectoryNotEmpty
                : DokanResult.Success;
        }

        public NtStatus DeleteFile([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            // we just check here if we could delete the file - the true deletion is in Cleanup
            string filePath = GetMinePath(fileName);

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
        }

        public NtStatus FindFiles([NotNull] string fileName, out IList<FileInformation> files, [NotNull] IDokanFileInfo info) {
            // This function is not called because FindFilesWithPattern is implemented
            // Return DokanResult.NotImplemented in FindFilesWithPattern to make FindFiles called
            return FindFilesWithPattern(fileName, "*", out files, info);
        }

        public NtStatus FindFilesWithPattern([NotNull] string fileName, [NotNull] string searchPattern, out IList<FileInformation> files, [NotNull] IDokanFileInfo info) {
            files = new DirectoryInfo(GetMinePath(fileName))
                   .EnumerateFileSystemInfos()
                   .Where(finfo => finfo != null && DokanHelper.DokanIsNameInExpression(searchPattern, finfo.Name, true))
                   .Select(finfo => new FileInformation {
                        Attributes = finfo.Attributes,
                        CreationTime = finfo.CreationTime,
                        LastAccessTime = finfo.LastAccessTime,
                        LastWriteTime = finfo.LastWriteTime,
                        Length = (finfo as FileInfo)?.Length ?? 0,
                        FileName = finfo.Name
                    }).ToArray();
            return DokanResult.Success;
        }

        public NtStatus FindStreams([NotNull] string fileName, out IList<FileInformation> streams, [NotNull] IDokanFileInfo info) {
            streams = new FileInformation[0];
            return DokanResult.NotImplemented;
        }

        public NtStatus FlushFileBuffers([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            try {
                FileStream stream = (FileStream) info.Context;
                Debug.Assert(stream != null, nameof(stream) + " != null");
                stream.Flush();
                return DokanResult.Success;
            } catch (IOException) {
                return DokanResult.DiskFull;
            }
        }

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, [NotNull] IDokanFileInfo info) {
            DriveInfo dinfo = DriveInfo.GetDrives().Single(di => di != null && string.Equals(di.RootDirectory.Name, Path.GetPathRoot(_MinePath + "\\"), StringComparison.OrdinalIgnoreCase));
            Debug.Assert(dinfo != null, nameof(dinfo) + " != null");
            freeBytesAvailable = dinfo.TotalFreeSpace;
            totalNumberOfBytes = dinfo.TotalSize;
            totalNumberOfFreeBytes = dinfo.AvailableFreeSpace;
            return DokanResult.Success;
        }

        public NtStatus GetFileInformation([NotNull] string fileName, out FileInformation fileInfo, [NotNull] IDokanFileInfo info) {
            // may be called with info.Context == null, but usually it isn't
            string filePath = GetMinePath(fileName);
            FileSystemInfo finfo = new FileInfo(filePath);
            if (!finfo.Exists) {
                finfo = new DirectoryInfo(filePath);
            }

            fileInfo = new FileInformation {
                FileName = fileName,
                Attributes = finfo.Attributes,
                CreationTime = finfo.CreationTime,
                LastAccessTime = finfo.LastAccessTime,
                LastWriteTime = finfo.LastWriteTime,
                Length = (finfo as FileInfo)?.Length ?? 0
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
            return DokanResult.NotImplemented;
        }

        public NtStatus Mounted([NotNull] IDokanFileInfo info) {
            return DokanResult.Success;
        }

        public NtStatus MoveFile([NotNull] string oldName, [NotNull] string newName, bool replace, [NotNull] IDokanFileInfo info) {
            string oldpath = GetMinePath(oldName);
            string newpath = GetMinePath(newName);

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

                    if (info.IsDirectory) {
                        // Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
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
            FileStream stream = info.Context as FileStream;
            if (stream == null) {
                using (stream = new FileStream(GetMinePath(fileName), FileMode.Open, System.IO.FileAccess.Read)) {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            } else {
                lock (stream) {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            }

            return DokanResult.Success;
        }

        public NtStatus SetAllocationSize([NotNull] string fileName, long length, [NotNull] IDokanFileInfo info) {
            try {
                FileStream stream = (FileStream) info.Context;
                Debug.Assert(stream != null, nameof(stream) + " != null");
                stream.SetLength(length);
                return DokanResult.Success;
            } catch (IOException) {
                return DokanResult.DiskFull;
            }
        }

        public NtStatus SetEndOfFile([NotNull] string fileName, long length, [NotNull] IDokanFileInfo info) {
            try {
                FileStream stream = (FileStream) info.Context;
                Debug.Assert(stream != null, nameof(stream) + " != null");
                stream.SetLength(length);
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
                    File.SetAttributes(GetMinePath(fileName), attributes);
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

                    throw Marshal.GetExceptionForHR(Marshal.GetLastWin32Error()) ?? new Exception("Unknown WIN32 error.");
                }

                string filePath = GetMinePath(fileName);

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
            return DokanResult.NotImplemented;
        }

        public NtStatus Unmounted([NotNull] IDokanFileInfo info) {
            return DokanResult.Success;
        }

        public NtStatus WriteFile([NotNull] string fileName, [NotNull] byte[] buffer, out int bytesWritten, long offset, [NotNull] IDokanFileInfo info) {
            FileStream stream = info.Context as FileStream;
            if (stream == null) {
                using (stream = new FileStream(GetMinePath(fileName), FileMode.Open, System.IO.FileAccess.Write)) {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                    bytesWritten = buffer.Length;
                }
            } else {
                lock (stream) {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                }

                bytesWritten = buffer.Length;
            }

            return DokanResult.Success;
        }

        [NotNull]
        private string GetMinePath([NotNull] string fileName) {
            return _MinePath + fileName;
        }


       
    }
}
