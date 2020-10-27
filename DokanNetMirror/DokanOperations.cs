using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using DokanNet;
using JetBrains.Annotations;
using static DokanNet.FormatProviders;
using FileAccess = DokanNet.FileAccess;

namespace DokanNetMirror
{
    internal partial class DokanOperations : IDokanOperations
    {
        public void Cleanup([NotNull] string fileName, [NotNull] IDokanFileInfo info) {
#if TRACE
            if (info.Context != null) {
                Console.WriteLine(DokanFormat($"{nameof(Cleanup)}('{fileName}', {info} - entering"));
            }
#endif

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

        public NtStatus CreateFile([NotNull] string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, [NotNull] IDokanFileInfo info) {
            NtStatus result = DokanResult.Success;
            string filePath = GetBasePath(fileName);

            if (info.IsDirectory) {
                try {
                    switch (mode) {
                        case FileMode.Open:
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

                        case FileMode.CreateNew:
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
                } catch (UnauthorizedAccessException) {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, DokanResult.AccessDenied);
                }
            } else {
                bool pathExists = true;
                bool pathIsDirectory = false;

                bool readWriteAttributes = (access & DataAccess) == 0;
                bool readAccess = (access & DataWriteAccess) == 0;

                try {
                    pathExists = Directory.Exists(filePath) || File.Exists(filePath);
                    pathIsDirectory = pathExists ? File.GetAttributes(filePath).HasFlag(FileAttributes.Directory) : false;
                } catch (IOException) {
                }

                switch (mode) {
                    case FileMode.Open:

                        if (pathExists) {
                            // check if driver only wants to read attributes, security info, or open directory
                            if (readWriteAttributes || pathIsDirectory) {
                                if (pathIsDirectory && (access & FileAccess.Delete) == FileAccess.Delete && (access & FileAccess.Synchronize) != FileAccess.Synchronize)
                                    //It is a DeleteFile request on a directory
                                {
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
                } catch (UnauthorizedAccessException) { // don't have access rights
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
            }

            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes, result);
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

        public NtStatus FindFiles([NotNull] string fileName, out IList<FileInformation> files, [NotNull] IDokanFileInfo info) {
            // This function is not called because FindFilesWithPattern is implemented
            // Return DokanResult.NotImplemented in FindFilesWithPattern to make FindFiles called
            return FindFilesWithPattern(fileName, "*", out files, info);
        }

        public NtStatus FindFilesWithPattern([NotNull] string fileName, [NotNull] string searchPattern, [NotNull] out IList<FileInformation> files, [NotNull] IDokanFileInfo info) {
            files =
                new DirectoryInfo(GetBasePath(fileName))
                   .EnumerateFileSystemInfos()
                   .Where(finfo => {
                        Debug.Assert(finfo != null, nameof(finfo) + " != null");
                        return DokanHelper.DokanIsNameInExpression(searchPattern, finfo.Name, true);
                    })
                   .Select(finfo => new FileInformation {
                        Attributes = finfo.Attributes,
                        CreationTime = finfo.CreationTime,
                        LastAccessTime = finfo.LastAccessTime,
                        LastWriteTime = finfo.LastWriteTime,
                        Length = (finfo as FileInfo)?.Length ?? 0,
                        FileName = finfo.Name
                    }).ToArray();

            return Trace(nameof(FindFilesWithPattern), fileName, info, DokanResult.Success);
        }

        public NtStatus GetFileInformation([NotNull] string fileName, out FileInformation fileInfo, [NotNull] IDokanFileInfo info) {
            // may be called with info.Context == null, but usually it isn't
            string filePath = GetBasePath(fileName);
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
            return Trace(nameof(GetFileInformation), fileName, info, DokanResult.Success);
        }

        public NtStatus GetFileSecurity([NotNull] string fileName, out FileSystemSecurity security, AccessControlSections sections, [NotNull] IDokanFileInfo info) {
            try {
                security = info.IsDirectory
                    ? (FileSystemSecurity) Directory.GetAccessControl(GetBasePath(fileName))
                    : File.GetAccessControl(GetBasePath(fileName));
                return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.Success, sections.ToString());
            } catch (UnauthorizedAccessException) {
                security = null;
                return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.AccessDenied, sections.ToString());
            }
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

                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName, replace);
                }

                if (replace) {
                    info.Context = null;

                    if (info.IsDirectory) //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
                    {
                        return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName, true);
                    }

                    File.Delete(newpath);
                    File.Move(oldpath, newpath);
                    return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName, true);
                }
            } catch (UnauthorizedAccessException) {
                return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName, replace);
            }

            return Trace(nameof(MoveFile), oldName, info, DokanResult.FileExists, newName, false);
        }

        public NtStatus ReadFile([NotNull] string fileName, [NotNull] byte[] buffer, out int bytesRead, long offset, [NotNull] IDokanFileInfo info) {
            FileStream fileStream = info.Context as FileStream;
            if (fileStream == null) // memory mapped read
            {
                _Logger.Info("ReadFile: NO Context");
                using (FileStream stream = new FileStream(GetBasePath(fileName), FileMode.Open, System.IO.FileAccess.Read)) {
                    stream.Position = offset;
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                }
            } else // normal read
            {
                //lock: Protect from overlapped read
                lock (fileStream) {
                    fileStream.Position = offset;
                    bytesRead = fileStream.Read(buffer, 0, buffer.Length);
                }
            }

            return Trace(nameof(ReadFile), fileName, info, DokanResult.Success, "out " + bytesRead, offset);
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

        public NtStatus SetFileSecurity([NotNull] string fileName, [NotNull] FileSystemSecurity security, AccessControlSections sections, [NotNull] IDokanFileInfo info) {
            try {
                if (info.IsDirectory) {
                    Directory.SetAccessControl(GetBasePath(fileName), (DirectorySecurity) security);
                } else {
                    File.SetAccessControl(GetBasePath(fileName), (FileSecurity) security);
                }

                return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.Success, sections.ToString());
            } catch (UnauthorizedAccessException) {
                return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.AccessDenied, sections.ToString());
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

                    throw Marshal.GetExceptionForHR(Marshal.GetLastWin32Error()) ?? new Exception("Unknown Win32 Error");
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
            FileStream fileStream = info.Context as FileStream;
            if (fileStream == null) {
                _Logger.Info("WriteFile: NO Context");
                using (FileStream stream = new FileStream(GetBasePath(fileName), FileMode.Open, System.IO.FileAccess.Write)) {
                    stream.Position = offset;
                    stream.Write(buffer, 0, buffer.Length);
                    bytesWritten = buffer.Length;
                }
            } else {
                //lock: Protect from overlapped write
                lock (fileStream) {
                    fileStream.Position = offset;
                    fileStream.Write(buffer, 0, buffer.Length);
                }

                bytesWritten = buffer.Length;
            }

            return Trace(nameof(WriteFile), fileName, info, DokanResult.Success, "out " + bytesWritten, offset);
        }
    }
}
