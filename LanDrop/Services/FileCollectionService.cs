// Services/FileCollectionService.cs
// Builds a flat list of FileEntry objects from dropped files/folders

using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
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
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                try
                {
                    if (Directory.Exists(path))
                    {
                        var fullPath = Path.GetFullPath(path);
                        var cleanDir = Path.TrimEndingDirectorySeparator(fullPath);
                        var basePath = Path.GetDirectoryName(cleanDir) ?? cleanDir;
                        AddDirectory(cleanDir, basePath, result);
                    }
                    else if (File.Exists(path))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(path);
                            result.Add(new FileEntry
                            {
                                FullPath     = path,
                                RelativePath = Path.GetFileName(path),
                                SizeBytes    = fileInfo.Length
                            });
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                        {
                            // Skip inaccessible file
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    // Skip inaccessible path
                }
            }
            return result;
        }

        private static void AddDirectory(string dir, string basePath, List<FileEntry> result)
        {
            var stack = new Stack<string>();
            stack.Push(dir);

            while (stack.Count > 0)
            {
                var currentDir = stack.Pop();

                // Enumerate subdirectories
                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                    {
                        stack.Push(subDir);
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    // Skip inaccessible directory
                }

                // Enumerate files
                try
                {
                    foreach (var file in Directory.EnumerateFiles(currentDir))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var relative = Path.GetRelativePath(basePath, file);
                            result.Add(new FileEntry
                            {
                                FullPath     = file,
                                RelativePath = relative,
                                SizeBytes    = fileInfo.Length
                            });
                        }
                        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                        {
                            // Skip inaccessible or locked file
                        }
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
                {
                    // Skip inaccessible directory file enumeration
                }
            }
        }
    }
}
