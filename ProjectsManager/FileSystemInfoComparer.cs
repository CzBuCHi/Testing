using System.Collections.Generic;
using System.IO;

namespace ProjectsManager
{
    public class FileSystemInfoComparer : IEqualityComparer<FileSystemInfo> {
        public bool Equals(FileSystemInfo x, FileSystemInfo y) {
            return x != null && y != null && x.Name == y.Name;
        }

        public int GetHashCode(FileSystemInfo obj) {
            return obj.Name.GetHashCode();
        }
    }
}
