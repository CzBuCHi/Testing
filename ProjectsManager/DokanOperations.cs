using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DokanNet;
using JetBrains.Annotations;
using FileAccess = DokanNet.FileAccess;

namespace ProjectsManager
{
    public class DokanOperations : DokanOperationsBase, IDokanOperations
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

            Trace(nameof(Cleanup), fileName, info, DokanResult.Success);
        }

        public NtStatus DeleteDirectory([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            return Trace(nameof(DeleteDirectory), fileName, info, Directory.EnumerateFileSystemEntries(GetBasePath(fileName)).Any() ? DokanResult.DirectoryNotEmpty : DokanResult.Success);
            // if dir is not empty it can't be deleted
        }

        public NtStatus DeleteFile([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
            string filePath = GetBasePath(fileName);

            if (Directory.Exists(filePath)) {
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);
            }

            if (!File.Exists(filePath)) {
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.FileNotFound);
            }

            if (File.GetAttributes(filePath).HasFlag(FileAttributes.Directory)) {
                return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);
            }

            return Trace(nameof(DeleteFile), fileName, info, DokanResult.Success);
            // we just check here if we could delete the file - the true deletion is in Cleanup
        }

        public override NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info) {
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

            return Trace(nameof(FindFilesWithPattern), fileName, info, DokanResult.Success);
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
            return Trace(nameof(GetFileInformation), fileName, info, DokanResult.Success, fsi.FullName);
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

                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName, replace.ToString(CultureInfo.InvariantCulture));
                }

                if (replace) {
                    info.Context = null;

                    //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
                    if (info.IsDirectory) {
                        return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName, true);
                    }

                    File.Delete(newpath);
                    File.Move(oldpath, newpath);
                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName, true);
                }
            } catch (UnauthorizedAccessException) {
                return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName, replace.ToString(CultureInfo.InvariantCulture));
            }

            return Trace(nameof(MoveFile), oldName, info, DokanResult.FileExists, newName, false);
        }

        public NtStatus ReadFile([NotNull] string fileName, [NotNull] byte[] buffer, out int bytesRead, long offset, [NotNull] IDokanFileInfo info) {
            if (info.Context is FileStream contextStream) {
                // Protect from overlapped read
                lock (contextStream) {
                    contextStream.Position = offset;
                    bytesRead = contextStream.Read(buffer, 0, buffer.Length);
                }
            } else {
                bytesRead = 0;
                return DokanResult.NotImplemented;
                // memory mapped read
                using (FileStream stream = new FileStream(GetBasePath(fileName), FileMode.Open, System.IO.FileAccess.Read)) {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            }

            return Trace(nameof(ReadFile), fileName, info, DokanResult.Success, "out " + bytesRead, offset.ToString(CultureInfo.InvariantCulture));
        }

        public NtStatus SetFileAttributes([NotNull] string fileName, FileAttributes attributes, [NotNull] IDokanFileInfo info) {
            try {
                // MS-FSCC 2.6 File Attributes : There is no file attribute with the value 0x00000000
                // because a value of 0x00000000 in the FileAttributes field means that the file attributes for this file MUST NOT be changed when setting basic information for the file
                if (attributes != 0) {
                    File.SetAttributes(GetBasePath(fileName), attributes);
                }

                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.Success, attributes.ToString());
            } catch (UnauthorizedAccessException) {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.AccessDenied, attributes.ToString());
            } catch (FileNotFoundException) {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.FileNotFound, attributes.ToString());
            } catch (DirectoryNotFoundException) {
                return Trace(nameof(SetFileAttributes), fileName, info, DokanResult.PathNotFound, attributes.ToString());
            }
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

                return Trace(nameof(SetFileTime), fileName, info, DokanResult.Success, creationTime, lastAccessTime, lastWriteTime);
            } catch (UnauthorizedAccessException) {
                return Trace(nameof(SetFileTime), fileName, info, DokanResult.AccessDenied, creationTime, lastAccessTime, lastWriteTime);
            } catch (FileNotFoundException) {
                return Trace(nameof(SetFileTime), fileName, info, DokanResult.FileNotFound, creationTime, lastAccessTime, lastWriteTime);
            }
        }

        public NtStatus WriteFile([NotNull] string fileName, [NotNull] byte[] buffer, out int bytesWritten, long offset, [NotNull] IDokanFileInfo info) {
            if (info.Context is FileStream contentStream) {
                // Protect from overlapped write
                lock (contentStream) {
                    contentStream.Position = offset;
                    contentStream.Write(buffer, 0, buffer.Length);
                }

                bytesWritten = buffer.Length;
            } else {
                bytesWritten = 0;
                return DokanResult.NotImplemented;
                using (FileStream stream = new FileStream(GetBasePath(fileName), FileMode.Open, System.IO.FileAccess.Write)) {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                    bytesWritten = buffer.Length;
                }
            }

            return Trace(nameof(WriteFile), fileName, info, DokanResult.Success, "out " + bytesWritten, offset.ToString(CultureInfo.InvariantCulture));
        }

        protected override NtStatus CreateFile_Directory(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info) {
            NtStatus result = DokanResult.Success;

            try {
                switch (mode) {
                    case FileMode.Open: {
                        string filePath = GetBasePath(fileName);
                        if (!Directory.Exists(filePath)) {
                            try {
                                if (!File.GetAttributes(filePath).HasFlag(FileAttributes.Directory)) {
                                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.NotADirectory);
                                }
                            } catch (Exception) {
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                            }

                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.PathNotFound);
                        }

                        // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                        new DirectoryInfo(filePath).EnumerateFileSystemInfos().Any();
                        // you can't list the directory
                        break;
                    }
                    case FileMode.CreateNew: {
                        string filePath = GetBasePath(fileName);
                        if (Directory.Exists(filePath)) {
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.FileExists);
                        }

                        try {
                            // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                            File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.AlreadyExists);
                        } catch (IOException) {
                        }

                        Directory.CreateDirectory(GetBasePath(fileName));
                        break;
                    }
                }
            } catch (UnauthorizedAccessException) {
                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.AccessDenied);
            }

            return result;
        }

        protected override NtStatus CreateFile_File(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info) {
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
                                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.AccessDenied);
                            }

                            info.IsDirectory = pathIsDirectory;
                            info.Context = new object();
                            // must set it to someting if you return DokanError.Success

                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.Success);
                        }
                    } else {
                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                    }

                    break;

                case FileMode.CreateNew:
                    if (pathExists) {
                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.FileExists);
                    }

                    break;

                case FileMode.Truncate:
                    if (!pathExists) {
                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
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
            } catch (UnauthorizedAccessException) // don't have access rights
            {
                if (info.Context is FileStream fileStream) {
                    // returning AccessDenied cleanup and close won't be called,
                    // so we have to take care of the stream now
                    fileStream.Dispose();
                    info.Context = null;
                }

                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.AccessDenied);
            } catch (DirectoryNotFoundException) {
                return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.PathNotFound);
            } catch (Exception ex) {
                uint hr = (uint) Marshal.GetHRForException(ex);
                switch (hr) {
                    case 0x80070020: //Sharing violation
                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.SharingViolation);
                    default:
                        throw;
                }
            }

            return result;
        }

        [NotNull]
        protected string GetBasePath([NotNull] string fileName) {
            return _BasePath + fileName;
        }

        [NotNull]
        protected string GetMinePath([NotNull] string fileName) {
            return _MinePath + fileName;
        }
    }
}
