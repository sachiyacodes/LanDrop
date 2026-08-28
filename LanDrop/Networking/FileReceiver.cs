// Networking/FileReceiver.cs
// TCP listener that accepts incoming LanDrop connections and saves files

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LanDrop.Models;
using Microsoft.Extensions.Logging;

namespace LanDrop.Networking
{
    /// <summary>
    /// Raised when an incoming transfer request arrives and needs user acceptance.
    /// Set <see cref="Accepted"/> to false to reject, or populate <see cref="SaveDirectory"/>.
    /// </summary>
    public class IncomingTransferEventArgs : EventArgs
    {
        public required HelloMsg    Hello          { get; init; }
        public required string      RemoteAddress  { get; init; }
        public bool                 Accepted       { get; set; } = true;
        public string               Reason         { get; set; } = string.Empty;
        public required string      SaveDirectory  { get; set; }
    }

    /// <summary>
    /// Listens on a TCP port for incoming LanDrop transfer sessions.
    /// </summary>
    public class FileReceiver : IDisposable
    {
        private readonly AppSettings _settings;
        private readonly ILogger     _logger;
        private TcpListener?         _listener;
        private CancellationTokenSource? _cts;

        // Events
        public event EventHandler<IncomingTransferEventArgs>?   IncomingTransfer;
        public event Action<TransferProgress>?                   Progress;
        public event Action<string, long, bool>?                 TransferCompleted; // (relPath, sizeBytes, success)
        public event Action?                                     SessionCompleted;
        public event Action<string>?                             TransferError;

        // Pause gate
        private readonly object _pauseLock = new();
        private TaskCompletionSource<bool>? _pauseTcs;
        private volatile bool _paused;

        public FileReceiver(AppSettings settings, ILogger<FileReceiver> logger)
        {
            _settings = settings;
            _logger   = logger;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void StartListening()
        {
            _cts      = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, _settings.TransferPort);
            _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Start();

            _ = AcceptLoopAsync(_cts.Token);
            _logger.LogInformation("FileReceiver listening on TCP port {Port}.", _settings.TransferPort);
        }

        public void StopListening()
        {
            _cts?.Cancel();
            _listener?.Stop();
            Resume();
        }

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
                await waitTask.WaitAsync(ct);
            }
        }

        // ── Accept loop ───────────────────────────────────────────────────────

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener!.AcceptTcpClientAsync(ct);
                    _ = HandleClientAsync(client, ct);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    _logger.LogWarning(ex, "Accept error.");
                }
            }
        }

        // ── Per-client handler ────────────────────────────────────────────────

        private async Task HandleClientAsync(TcpClient tcp, CancellationToken ct)
        {
            var remote = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address.ToString();
            _logger.LogInformation("Incoming connection from {Remote}.", remote);

            tcp.ReceiveBufferSize = _settings.SocketBufferSize;
            tcp.SendBufferSize    = _settings.SocketBufferSize;
            tcp.NoDelay = true;

            string? activeTempPath = null;

            using (tcp)
            using (var stream = tcp.GetStream())
            {
                try
                {
                    // ── Handshake ─────────────────────────────────────────────
                    var (msg1Type, msg1Payload) = await stream.ReadFrameAsync(ct);
                    if (msg1Type != MsgType.Hello)
                    {
                        await SendError(stream, "UNEXPECTED_MSG", "Expected Hello.", ct);
                        return;
                    }

                    var hello = FrameHelper.FromJson<HelloMsg>(msg1Payload);
                    _logger.LogInformation("Hello from {Name}, {N} file(s), {Bytes} bytes.",
                        hello.SenderName, hello.FileCount, hello.TotalBytes);

                    // PIN check
                    if (!string.IsNullOrEmpty(_settings.PinCode))
                    {
                        await stream.WriteJsonFrameAsync(MsgType.PinChallenge,
                            new { required = true }, ct);

                        var (pinType, pinPayload) = await stream.ReadFrameAsync(ct);
                        if (pinType != MsgType.PinResponse)
                        {
                            await SendError(stream, "PIN_REQUIRED", "PIN response expected.", ct);
                            return;
                        }
                        var receivedPin = Encoding.UTF8.GetString(pinPayload);
                        if (receivedPin != _settings.PinCode)
                        {
                            await stream.WriteJsonFrameAsync(MsgType.HelloAck,
                                new HelloAckMsg(false, "Incorrect PIN.", string.Empty), ct);
                            return;
                        }
                    }

                    // Ask UI to accept/reject
                    var args = new IncomingTransferEventArgs
                    {
                        Hello         = hello,
                        RemoteAddress = remote,
                        SaveDirectory = _settings.ReceiveSavePath
                    };
                    IncomingTransfer?.Invoke(this, args);

                    if (!args.Accepted)
                    {
                        await stream.WriteJsonFrameAsync(MsgType.HelloAck,
                            new HelloAckMsg(false, args.Reason, string.Empty), ct);
                        return;
                    }

                    await stream.WriteJsonFrameAsync(MsgType.HelloAck,
                        new HelloAckMsg(true, string.Empty, args.SaveDirectory), ct);

                    // ── Receive files ─────────────────────────────────────────
                    long totalBytes = hello.TotalBytes;
                    long received   = 0;
                    var  speedometer = new Speedometer();
                    int  chunkSize   = Math.Max(_settings.ChunkSize, 4 * 1024 * 1024);
                    byte[] chunkBuf  = System.Buffers.ArrayPool<byte>.Shared.Rent(chunkSize);
                    var  hasher      = new FastHasher();

                    try
                    {
                        for (int i = 0; i < hello.FileCount; i++)
                        {
                            var (hdrType, hdrPayload) = await stream.ReadFrameAsync(ct);
                            if (hdrType == MsgType.SessionDone) break;
                            if (hdrType == MsgType.Cancel)
                            {
                                _logger.LogWarning("Transfer cancelled by sender.");
                                TransferError?.Invoke("Cancelled by sender.");
                                return;
                            }
                            if (hdrType != MsgType.FileHeader)
                            {
                                await SendError(stream, "UNEXPECTED_MSG", "Expected FileHeader.", ct);
                                return;
                            }

                            var fileHeader = FrameHelper.FromJson<FileHeaderMsg>(hdrPayload);
                            _logger.LogInformation("Receiving file [{Idx}] {Name} ({Size} bytes).",
                                fileHeader.FileIndex, fileHeader.RelativePath, fileHeader.SizeBytes);

                            // Prepare destination path — sanitise relative path
                            string safePath = SanitisePath(fileHeader.RelativePath);
                            string destPath = Path.Combine(args.SaveDirectory, safePath);
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                            // Write to a temp file first, then rename on success
                            string tempPath = destPath + ".landrop_tmp";
                            activeTempPath = tempPath;
                            bool   success  = false;
                            string? actualHash = null;
                            bool cancelled = false;

                            try
                            {
                                hasher.Reset();

                                var fsOptions = new FileStreamOptions
                                {
                                    Mode = FileMode.Create,
                                    Access = FileAccess.Write,
                                    Share = FileShare.None,
                                    Options = FileOptions.Asynchronous,
                                    BufferSize = chunkSize
                                };

                                using (var fs = new FileStream(tempPath, fsOptions))
                                {
                                    // Contiguous NVMe SSD cluster pre-allocation
                                    if (fileHeader.SizeBytes > 0)
                                    {
                                        fs.SetLength(fileHeader.SizeBytes);
                                    }

                                    long fileBytesReceived = 0;

                                    while (fileBytesReceived < fileHeader.SizeBytes)
                                    {
                                        // Pause gate
                                        await WaitIfPausedAsync(ct);

                                        var (chunkType, chunkLen) = await stream.ReadHeaderAsync(ct);

                                        if (chunkType == MsgType.Cancel)
                                        {
                                            _logger.LogWarning("Transfer cancelled by sender.");
                                            TransferError?.Invoke("Cancelled by sender.");
                                            cancelled = true;
                                            break;
                                        }

                                        if (chunkType == MsgType.FileDone)
                                        {
                                            _logger.LogWarning("File ended prematurely on sender at {Received}/{Expected} bytes.",
                                                fileBytesReceived, fileHeader.SizeBytes);

                                            byte[] prematurePayload = chunkLen > 0 ? new byte[chunkLen] : Array.Empty<byte>();
                                            if (chunkLen > 0)
                                                await StreamExtensions.ReadExactlyAsync(stream, prematurePayload, 0, chunkLen, ct);

                                            // Send ChecksumAck with failure
                                            await stream.WriteJsonFrameAsync(MsgType.ChecksumAck,
                                                new ChecksumAckMsg(i, false, hasher.GetHashHex()), ct);

                                            TransferCompleted?.Invoke(fileHeader.RelativePath, fileHeader.SizeBytes, false);
                                            cancelled = true;
                                            break;
                                        }

                                        if (chunkType != MsgType.DataChunk && chunkType != MsgType.CompressedChunk)
                                        {
                                            string errDetail = $"Expected DataChunk or CompressedChunk but got 0x{chunkType:X2} (ASCII '{(chunkType >= 32 && chunkType < 127 ? (char)chunkType : '?')}') with len {chunkLen} at {fileBytesReceived}/{fileHeader.SizeBytes} bytes.";
                                            _logger.LogError("{Error}", errDetail);
                                            await SendError(stream, "UNEXPECTED_MSG", errDetail, ct);
                                            throw new InvalidOperationException(errDetail);
                                        }

                                        // Read directly into pooled buffer
                                        if (chunkLen > chunkBuf.Length)
                                        {
                                            System.Buffers.ArrayPool<byte>.Shared.Return(chunkBuf);
                                            chunkBuf = System.Buffers.ArrayPool<byte>.Shared.Rent(chunkLen);
                                        }

                                        await StreamExtensions.ReadExactlyAsync(stream, chunkBuf, 0, chunkLen, ct);

                                        if (chunkType == MsgType.CompressedChunk)
                                        {
                                            int uncompressedLen = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(chunkBuf.AsSpan(0, 4));
                                            byte[] decompBuf = System.Buffers.ArrayPool<byte>.Shared.Rent(uncompressedLen);
                                            try
                                            {
                                                FastCompress.Decompress(chunkBuf.AsSpan(4, chunkLen - 4), decompBuf.AsSpan(0, uncompressedLen));
                                                hasher.Append(decompBuf.AsSpan(0, uncompressedLen));
                                                await fs.WriteAsync(decompBuf.AsMemory(0, uncompressedLen), ct);

                                                fileBytesReceived += uncompressedLen;
                                                received          += uncompressedLen;
                                                speedometer.Add(uncompressedLen);
                                            }
                                            finally
                                            {
                                                System.Buffers.ArrayPool<byte>.Shared.Return(decompBuf);
                                            }
                                        }
                                        else
                                        {
                                            hasher.Append(chunkBuf.AsSpan(0, chunkLen));
                                            await fs.WriteAsync(chunkBuf.AsMemory(0, chunkLen), ct);

                                            fileBytesReceived += chunkLen;
                                            received          += chunkLen;
                                            speedometer.Add(chunkLen);
                                        }

                                        Progress?.Invoke(new TransferProgress(
                                            totalBytes, received,
                                            speedometer.BytesPerSecond,
                                            i, fileHeader.RelativePath));
                                    }

                                    if (!cancelled)
                                    {
                                        await fs.FlushAsync(ct);
                                    }
                                }

                                if (cancelled)
                                {
                                    return;
                                }

                                actualHash = hasher.GetHashHex();

                                // Consume FileDone message before sending ChecksumAck
                                var (doneType, donePayload) = await stream.ReadFrameAsync(ct);
                                if (doneType == MsgType.Cancel)
                                {
                                    _logger.LogWarning("Transfer cancelled by sender.");
                                    TransferError?.Invoke("Cancelled by sender.");
                                    return;
                                }
                                if (doneType != MsgType.FileDone)
                                {
                                    await SendError(stream, "UNEXPECTED_MSG", "Expected FileDone.", ct);
                                    throw new InvalidOperationException($"Expected FileDone but got 0x{doneType:X2}");
                                }

                                var doneMsg = FrameHelper.FromJson<FileDoneMsg>(donePayload);
                                bool hashMatch = string.Equals(actualHash,
                                    doneMsg.Checksum, StringComparison.OrdinalIgnoreCase);

                                if (hashMatch)
                                {
                                    // Rename temp → final
                                    if (File.Exists(destPath)) File.Delete(destPath);
                                    File.Move(tempPath, destPath);
                                    activeTempPath = null;
                                    success = true;
                                    _logger.LogInformation("File saved: {Path} (checksum OK).", destPath);
                                }
                                else
                                {
                                    if (File.Exists(tempPath)) File.Delete(tempPath);
                                    activeTempPath = null;
                                    _logger.LogError("Checksum MISMATCH! Expected {Expected}, got {Actual}.",
                                        doneMsg.Checksum, actualHash);
                                }

                                // Send pipelined checksum ack
                                await stream.WriteJsonFrameAsync(MsgType.ChecksumAck,
                                    new ChecksumAckMsg(i, hashMatch, actualHash), ct);

                                TransferCompleted?.Invoke(fileHeader.RelativePath, fileHeader.SizeBytes, success);
                            }
                            finally
                            {
                                if (activeTempPath != null && File.Exists(activeTempPath))
                                {
                                    try { File.Delete(activeTempPath); } catch { }
                                    activeTempPath = null;
                                }
                            }
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(chunkBuf);
                    }

                    _logger.LogInformation("Session complete from {Remote}.", remote);
                    SessionCompleted?.Invoke();
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling client {Remote}.", remote);
                    TransferError?.Invoke(ex.Message);
                    try { await SendError(stream, "INTERNAL_ERROR", ex.Message, ct); } catch { }
                }
                finally
                {
                    if (activeTempPath != null && File.Exists(activeTempPath))
                    {
                        try { File.Delete(activeTempPath); } catch { }
                        activeTempPath = null;
                    }
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Task SendError(Stream stream, string code, string msg, CancellationToken ct) =>
            stream.WriteJsonFrameAsync(MsgType.Error, new ErrorMsg(code, msg), ct);

        /// <summary>
        /// Strip any path-traversal attempts from a relative path sent by the peer.
        /// </summary>
        private static string SanitisePath(string relativePath)
        {
            // Normalise directory separators
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                       .Replace('\\', Path.DirectorySeparatorChar);

            // Split and filter out dangerous components
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            var safe  = new System.Collections.Generic.List<string>();
            foreach (var part in parts)
            {
                if (part is ".." or "." or "") continue;
                // Remove chars invalid in Windows file names
                var cleaned = string.Concat(part.Split(Path.GetInvalidFileNameChars()));
                if (!string.IsNullOrWhiteSpace(cleaned))
                    safe.Add(cleaned);
            }
            return safe.Count > 0
                ? Path.Combine(safe.ToArray())
                : "received_file";
        }

        // ── IDisposable ───────────────────────────────────────────────────────
        public void Dispose()
        {
            StopListening();
            _cts?.Dispose();
        }
    }
}
