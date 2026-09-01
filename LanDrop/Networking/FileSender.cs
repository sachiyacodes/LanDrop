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
        private readonly object _pauseLock = new();
        private TaskCompletionSource<bool>? _pauseTcs;
        private volatile bool _paused;

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

            foreach (var f in files)
            {
                f.TransferredBytes = 0;
            }

            Resume();

            using var tcp = new TcpClient();
            tcp.SendBufferSize    = Math.Max(_settings.SocketBufferSize, 8 * 1024 * 1024);
            tcp.ReceiveBufferSize = Math.Max(_settings.SocketBufferSize, 8 * 1024 * 1024);
            tcp.NoDelay = true;

            await tcp.ConnectAsync(host, port, ct);
            using var stream = tcp.GetStream();
            Exception? ackFailure = null;

            try
            {
                // ── Handshake ────────────────────────────────────────────────────
                await HandshakeAsync(stream, files, pin, ct);

                // ── Send files ───────────────────────────────────────────────────
                long totalBytes     = 0;
                foreach (var f in files) totalBytes += f.SizeBytes;

                long   sent          = 0;
                var    speedWindow   = new Speedometer();
                int    chunkSize     = Math.Max(_settings.ChunkSize, 4 * 1024 * 1024);
                byte[] buffer        = System.Buffers.ArrayPool<byte>.Shared.Rent(chunkSize);

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var ackToken = linkedCts.Token;

                // Start asynchronous background ACK processor for pipelined streaming
                var ackTask = Task.Run(async () =>
                {
                    try
                    {
                        for (int i = 0; i < files.Count; i++)
                        {
                            var (msgType, payload) = await stream.ReadFrameAsync(ackToken);
                            if (msgType == MsgType.ChecksumAck)
                            {
                                var ack = FrameHelper.FromJson<ChecksumAckMsg>(payload);
                                if (ack.FileIndex != i)
                                {
                                    _logger.LogError("Unexpected checksum ACK index {AckIndex}, expected {ExpectedIndex}.", ack.FileIndex, i);
                                    throw new InvalidDataException($"Unexpected checksum ACK index {ack.FileIndex}, expected {i}.");
                                }

                                if (!ack.HashMatch)
                                {
                                    _logger.LogError("Checksum MISMATCH on file {Index}!", ack.FileIndex);
                                    throw new InvalidDataException($"Checksum mismatch for file {ack.FileIndex}.");
                                }
                            }
                            else if (msgType == MsgType.Error)
                            {
                                var err = FrameHelper.FromJson<ErrorMsg>(payload);
                                throw new IOException($"Receiver error: {err.Message}");
                            }
                            else if (msgType == MsgType.Cancel)
                            {
                                throw new OperationCanceledException("Receiver cancelled the transfer.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        ackFailure = ex;
                        try { linkedCts.Cancel(); } catch { }
                        throw;
                    }
                }, ackToken);

                try
                {
                    var hasher = new FastHasher();

                    for (int i = 0; i < files.Count; i++)
                    {
                        var entry = files[i];
                        _logger.LogInformation("Streaming file {Index}/{Total}: {Name}", i + 1, files.Count, entry.RelativePath);

                        // Send header
                        var header = new FileHeaderMsg(i, entry.RelativePath, entry.SizeBytes);
                        await stream.WriteJsonFrameAsync(MsgType.FileHeader, header, ackToken);

                        hasher.Reset();

                        if (entry.SizeBytes > 0)
                        {
                            bool isPrecompressed = FastCompress.IsPrecompressedExtension(entry.RelativePath);
                            byte[]? compBuffer = !isPrecompressed ? System.Buffers.ArrayPool<byte>.Shared.Rent(chunkSize) : null;

                            try
                            {
                                // SequentialScan kernel cache hint for maximum NVMe/SSD read-ahead throughput
                                var fsOptions = new FileStreamOptions
                                {
                                    Mode = FileMode.Open,
                                    Access = FileAccess.Read,
                                    Share = FileShare.Read,
                                    Options = FileOptions.SequentialScan | FileOptions.Asynchronous,
                                    BufferSize = chunkSize
                                };

                                using (var fs = new FileStream(entry.FullPath, fsOptions))
                                {
                                    int read;
                                    while ((read = await fs.ReadAsync(buffer.AsMemory(0, chunkSize), ackToken)) > 0)
                                    {
                                        // Pause gate
                                        await WaitIfPausedAsync(ackToken);

                                        ackToken.ThrowIfCancellationRequested();

                                        // Compute hash on uncompressed source bytes
                                        hasher.Append(buffer.AsSpan(0, read));

                                        // Try real-time LZ4 compression if file is compressible
                                        int compLen = -1;
                                        if (compBuffer != null && read >= 128)
                                        {
                                            compLen = FastCompress.TryCompress(buffer.AsSpan(0, read), compBuffer.AsSpan(0, compBuffer.Length));
                                        }

                                        if (compLen > 0)
                                        {
                                            // Send compressed chunk (saves network bandwidth)
                                            await stream.WriteCompressedChunkFrameAsync(compBuffer!, 0, compLen, read, ackToken);
                                        }
                                        else
                                        {
                                            // Send raw chunk directly
                                            await stream.WriteChunkFrameAsync(MsgType.DataChunk, buffer, 0, read, ackToken);
                                        }

                                        sent += read;
                                        entry.TransferredBytes += read;
                                        speedWindow.Add(read);

                                        onProgress(new TransferProgress(
                                            totalBytes, sent,
                                            speedWindow.BytesPerSecond,
                                            i, entry.RelativePath));
                                    }
                                }
                            }
                            finally
                            {
                                if (compBuffer != null)
                                {
                                    System.Buffers.ArrayPool<byte>.Shared.Return(compBuffer);
                                }
                            }
                        }

                        // Compute final streaming hash on-the-fly
                        string checksum = hasher.GetHashHex();
                        entry.Sha256Hash = checksum;

                        // Signal end of this file with computed checksum
                        await stream.WriteJsonFrameAsync(MsgType.FileDone, new FileDoneMsg(i, entry.SizeBytes, checksum), ackToken);
                    }

                    // Await all pipelined ACKs to complete
                    await ackTask;

                    // All files done
                    await stream.WriteJsonFrameAsync(MsgType.SessionDone,
                        new { SessionId = Guid.NewGuid().ToString() }, ct);

                    _logger.LogInformation("All files streamed successfully.");
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (OperationCanceledException) when (ackFailure is not null && !ct.IsCancellationRequested)
            {
                _logger.LogError(ackFailure, "Transfer aborted due to ACK/verification failure.");
                throw ackFailure;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Transfer cancelled by user.");
                try
                {
                    using var cancelCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await stream.WriteFrameAsync(MsgType.Cancel, Array.Empty<byte>(), cancelCts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to send Cancel frame to receiver.");
                }
                throw;
            }
        }

        // ── Pause / Resume / Cancel ───────────────────────────────────────────

        public void Pause()
        {
            lock (_pauseLock)
            {
                if (!_paused)
                {
                    _paused = true;
                    _pauseTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }

        public void Resume()
        {
            lock (_pauseLock)
            {
                if (_paused)
                {
                    _paused = false;
                    _pauseTcs?.TrySetResult(true);
                    _pauseTcs = null;
                }
            }
        }

        private async Task WaitIfPausedAsync(CancellationToken ct)
        {
            Task? waitTask = null;
            lock (_pauseLock)
            {
                if (_paused && _pauseTcs != null)
                {
                    waitTask = _pauseTcs.Task;
                }
            }

            if (waitTask != null)
            {
                _logger.LogInformation("Transfer paused.");
                await waitTask.WaitAsync(ct);
                _logger.LogInformation("Transfer resumed.");
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
        private readonly Queue<(DateTime time, long bytes)> _samples = new();
        private const double WindowSeconds = 2.0;

        public void Add(long bytes)
        {
            var now = DateTime.UtcNow;
            _samples.Enqueue((now, bytes));
            Prune(now);
        }

        private void Prune(DateTime now)
        {
            var cutoff = now.AddSeconds(-WindowSeconds);
            while (_samples.Count > 0 && _samples.Peek().time < cutoff)
                _samples.Dequeue();
        }

        public double BytesPerSecond
        {
            get
            {
                var now = DateTime.UtcNow;
                Prune(now);
                if (_samples.Count < 2) return 0;

                long total = 0;
                DateTime oldest = DateTime.MaxValue;

                foreach (var s in _samples)
                {
                    total += s.bytes;
                    if (s.time < oldest) oldest = s.time;
                }

                double elapsed = (now - oldest).TotalSeconds;
                if (elapsed <= 0.0001) return 0;

                return total / elapsed;
            }
        }
    }
}
