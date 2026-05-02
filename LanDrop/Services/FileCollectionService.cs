// Services/FileCollectionService.cs
// Builds a flat list of FileEntry objects from dropped files/folders

using System.Collections.Generic;
using System.IO;
using LanDrop.Models;

namespace LanDrop.Services
{
    /// <summary>
    /// Converts a set of paths (files and/or folders) into a flat
    /// <see cref="FileEntry"/> list with correct relative paths for reconstruction.
    /// </summary>
    public static class FileCollectionService
    {
        /// <summary>
        /// Expand the given top-level paths into a flat list of FileEntry objects.
        /// For a folder, the relative path is relative to the folder's parent.
        /// </summary>
        public static List<FileEntry> Collect(IEnumerable<string> paths)
        {
            var result = new List<FileEntry>();
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                    AddDirectory(path, Path.GetDirectoryName(path)!, result);
                else if (File.Exists(path))
                    result.Add(new FileEntry
                    {
                        FullPath     = path,
                        RelativePath = Path.GetFileName(path),
                        SizeBytes    = new FileInfo(path).Length
                    });
            }
            return result;
        }

        private static void AddDirectory(string dir, string basePath, List<FileEntry> result)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                // relative path preserves folder hierarchy
                var relative = Path.GetRelativePath(basePath, file);
                result.Add(new FileEntry
                {
                    FullPath     = file,
                    RelativePath = relative,
                    SizeBytes    = new FileInfo(file).Length
                });
            }
        }
    }
}
