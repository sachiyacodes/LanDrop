// Networking/FileSender.cs
// Handles the TCP sender side: connect → handshake → stream files → verify checksums

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using LanDrop.Models;
using LanDrop.Networking;
using Microsoft.Extensions.Logging;

namespace LanDrop.Networking
{
    /// <summary>
    /// Progress update raised during a transfer.
    /// </summary>
    public record TransferProgress(
        long   TotalBytes,
        long   TransferredBytes,
        double SpeedBytesPerSecond,
        int    CurrentFileIndex,
        string CurrentFileName
    );

    /// <summary>
    /// Sends one or more files/folders to a remote LanDrop receiver over TCP.
    /// </summary>
    public class FileSender
    {
        private readonly AppSettings _settings;
        private readonly ILogger     _logger;

        // Pause/resume gate
        private volatile bool _paused;
        private readonly SemaphoreSlim _pauseGate = new(1, 1);

        public FileSender(AppSettings settings, ILogger<FileSender> logger)
        {
            _settings = settings;
            _logger   = logger;
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Connect to <paramref name="host"/>:<paramref name="port"/> and send all
        /// <paramref name="files"/>.  Raises <paramref name="onProgress"/> on each
        /// chunk and returns when all files are done (or throws on error/cancel).
        /// </summary>
        public async Task SendAsync(
            string                        host,
            int                           port,
            List<FileEntry>               files,
            string?                       pin,
            Action<TransferProgress>      onProgress,
            CancellationToken             ct)
        {
            _logger.LogInformation("Connecting to {Host}:{Port} to send {N} file(s).", host, port, files.Count);

            using var tcp    = new TcpClient();
            tcp.SendBufferSize    = _settings.SocketBufferSize;
            tcp.ReceiveBufferSize = _settings.SocketBufferSize;
            tcp.NoDelay = true;

            await tcp.ConnectAsync(host, port, ct);
            using var stream = tcp.GetStream();

            // ── Handshake ────────────────────────────────────────────────────
            await HandshakeAsync(stream, files, pin, ct);

            // ── Send files ───────────────────────────────────────────────────
            long totalBytes     = 0;
            foreach (var f in files) totalBytes += f.SizeBytes;

            long   sent          = 0;
            var    speedWindow   = new Speedometer();
            int    chunkSize     = _settings.ChunkSize;
            byte[] buffer        = new byte[chunkSize];

            for (int i = 0; i < files.Count; i++)
            {
                var entry = files[i];
                _logger.LogInformation("Sending file {Index}/{Total}: {Name}", i + 1, files.Count, entry.RelativePath);

                // Send header
                var header = new FileHeaderMsg(i, entry.RelativePath, entry.SizeBytes, entry.Sha256Hash!);
                await stream.WriteJsonFrameAsync(MsgType.FileHeader, header, ct);

                // Stream chunks
                using var fs = new FileStream(entry.FullPath, FileMode.Open, FileAccess.Read,
                                              FileShare.Read, chunkSize, useAsync: true);

                int read;
                while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    // Pause gate
                    if (_paused)
                    {
                        _logger.LogInformation("Transfer paused.");
                        await _pauseGate.WaitAsync(ct);
                        _pauseGate.Release();
                        _logger.LogInformation("Transfer resumed.");
                    }

                    ct.ThrowIfCancellationRequested();

                    await stream.WriteFrameAsync(MsgType.DataChunk,
                        read == buffer.Length ? buffer : buffer[..read], ct);

                    sent += read;
                    entry.TransferredBytes += read;
                    speedWindow.Add(read);

                    onProgress(new TransferProgress(
                        totalBytes, sent,
                        speedWindow.BytesPerSecond,
                        i, entry.RelativePath));
                }

                // Signal end of this file
                await stream.WriteJsonFrameAsync(MsgType.FileDone, new FileDoneMsg(i), ct);

                // Wait for checksum ack
                var (msgType, payload) = await stream.ReadFrameAsync(ct);
                if (msgType == MsgType.ChecksumAck)
                {
                    var ack = FrameHelper.FromJson<ChecksumAckMsg>(payload);
                    if (!ack.HashMatch)
                    {
                        _logger.LogError("Checksum MISMATCH on file {Index}! Expected {Expected}, got {Actual}.",
                            i, entry.Sha256Hash, ack.ActualHash);
                        throw new InvalidDataException($"Checksum mismatch for '{entry.RelativePath}'.");
                    }
                    _logger.LogInformation("Checksum OK for {Name}.", entry.RelativePath);
                }
                else if (msgType == MsgType.Error)
                {
                    var err = FrameHelper.FromJson<ErrorMsg>(payload);
                    throw new IOException($"Receiver error: {err.Message}");
                }
            }

            // All files done
            await stream.WriteJsonFrameAsync(MsgType.SessionDone,
                new { SessionId = Guid.NewGuid().ToString() }, ct);

            _logger.LogInformation("All files sent successfully.");
        }

        // ── Pause / Resume / Cancel ───────────────────────────────────────────

        public void Pause()
        {
            if (!_paused)
            {
                _paused = true;
                _pauseGate.Wait(); // acquire – blocks the transfer loop
            }
        }

        public void Resume()
        {
            if (_paused)
            {
                _paused = false;
                _pauseGate.Release(); // unblocks the transfer loop
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static async Task HandshakeAsync(
            Stream          stream,
            List<FileEntry> files,
            string?         pin,
            CancellationToken ct)
        {
            long total = 0;
            foreach (var f in files) total += f.SizeBytes;

            var hello = new HelloMsg(
                Guid.NewGuid().ToString(),
                Environment.MachineName,
                App.Version,
                files.Count,
                total,
                false // requiresPin is determined by sender knowing remote requires it
            );
            await stream.WriteJsonFrameAsync(MsgType.Hello, hello, ct);

            // Might receive PIN challenge first
            var (msgType, payload) = await stream.ReadFrameAsync(ct);

            if (msgType == MsgType.PinChallenge)
            {
                // Send PIN
                await stream.WriteFrameAsync(MsgType.PinResponse,
                    System.Text.Encoding.UTF8.GetBytes(pin ?? string.Empty), ct);

                (msgType, payload) = await stream.ReadFrameAsync(ct);
            }

            if (msgType != MsgType.HelloAck)
                throw new InvalidOperationException($"Unexpected message during handshake: 0x{msgType:X2}");

            var ack = FrameHelper.FromJson<HelloAckMsg>(payload);
            if (!ack.Accepted)
                throw new InvalidOperationException($"Transfer rejected by receiver: {ack.Reason}");
        }
    }

    // ── Speed measurement helper ──────────────────────────────────────────────

    internal class Speedometer
    {
        private readonly System.Collections.Generic.Queue<(DateTime time, long bytes)> _samples = new();
        private const double WindowSeconds = 2.0;

        public void Add(long bytes)
        {
            _samples.Enqueue((DateTime.UtcNow, bytes));
            var cutoff = DateTime.UtcNow.AddSeconds(-WindowSeconds);
            while (_samples.Count > 0 && _samples.Peek().time < cutoff)
                _samples.Dequeue();
        }

        public double BytesPerSecond
        {
            get
            {
                if (_samples.Count < 2) return 0;
                long total = 0;
                foreach (var s in _samples) total += s.bytes;
                return total / WindowSeconds;
            }
        }
    }
}
