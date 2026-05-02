// Services/FileHashService.cs
// Computes SHA-256 checksums for files before sending

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LanDrop.Models;
using Microsoft.Extensions.Logging;

namespace LanDrop.Services
{
    /// <summary>
    /// Computes SHA-256 hashes for a list of FileEntry objects.
    /// Reports progress via a callback so the UI can show a "hashing" phase.
    /// </summary>
    public class FileHashService
    {
        private readonly ILogger<FileHashService> _logger;
        private const int BufferSize = 1024 * 1024; // 1 MB read buffer

        public FileHashService(ILogger<FileHashService> logger) => _logger = logger;

        /// <summary>
        /// Compute SHA-256 for every file in <paramref name="files"/> and populate
        /// <see cref="FileEntry.Sha256Hash"/>.
        /// </summary>
        /// <param name="onProgress">Called with (filesHashed, totalFiles).</param>
        public async Task ComputeHashesAsync(
            List<FileEntry>        files,
            Action<int, int>?      onProgress = null,
            CancellationToken      ct         = default)
        {
            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var entry = files[i];
                _logger.LogDebug("Hashing {Path}…", entry.RelativePath);
                entry.Sha256Hash = await ComputeFileHashAsync(entry.FullPath, ct);
                onProgress?.Invoke(i + 1, files.Count);
            }
        }

        /// <summary>
        /// Return hex-encoded SHA-256 of a single file.
        /// </summary>
        public static async Task<string> ComputeFileHashAsync(
            string           path,
            CancellationToken ct = default)
        {
            using var sha256 = SHA256.Create();
            using var fs     = new FileStream(path, FileMode.Open, FileAccess.Read,
                                              FileShare.Read, BufferSize, useAsync: true);
            var buf   = new byte[BufferSize];
            int read;
            while ((read = await fs.ReadAsync(buf, 0, buf.Length, ct)) > 0)
                sha256.TransformBlock(buf, 0, read, null, 0);

            sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return BitConverter.ToString(sha256.Hash!).Replace("-", "").ToLowerInvariant();
        }
    }
}
