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
        public event Action<string, bool>?                       TransferCompleted; // (relPath, success)
        public event Action<string>?                             TransferError;

        // Pause gate
        private volatile bool _paused;
        private readonly SemaphoreSlim _pauseGate = new(1, 1);

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
        }

        public void Pause()
        {
            if (!_paused)
            {
                _paused = true;
                _pauseGate.Wait();
            }
        }

        public void Resume()
        {
            if (_paused)
            {
                _paused = false;
                _pauseGate.Release();
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
                catch (Exception ex)
                {
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

                    for (int i = 0; i < hello.FileCount; i++)
                    {
                        var (hdrType, hdrPayload) = await stream.ReadFrameAsync(ct);
                        if (hdrType == MsgType.SessionDone) break;
                        if (hdrType != MsgType.FileHeader)
                        {
                            await SendError(stream, "UNEXPECTED_MSG", "Expected FileHeader.", ct);
                            return;
                        }

                        var fileHeader = FrameHelper.FromJson<FileHeaderMsg>(hdrPayload);
                        _logger.LogInformation("Receiving file [{Idx}] {Name} ({Size} bytes).",
                            fileHeader.FileIndex, fileHeader.RelativePath, fileHeader.SizeBytes);

                        // Prepare destination path — sanitise relative path
                        string safePath  = SanitisePath(fileHeader.RelativePath);
                        string destPath  = Path.Combine(args.SaveDirectory, safePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                        // Write to a temp file first, then rename on success
                        string tempPath  = destPath + ".landrop_tmp";
                        bool   success   = false;
                        string? actualHash = null;

                        using (var sha256 = SHA256.Create())
                        using (var fs    = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                                                          FileShare.None, _settings.ChunkSize, true))
                        using (var cs    = new CryptoStream(fs, sha256, CryptoStreamMode.Write))
                        {
                            long fileBytesReceived = 0;

                            while (fileBytesReceived < fileHeader.SizeBytes)
                            {
                                // Pause gate
                                if (_paused)
                                {
                                    await _pauseGate.WaitAsync(ct);
                                    _pauseGate.Release();
                                }

                                var (chunkType, chunkData) = await stream.ReadFrameAsync(ct);

                                if (chunkType == MsgType.Cancel)
                                {
                                    _logger.LogWarning("Transfer cancelled by sender.");
                                    TransferError?.Invoke("Cancelled by sender.");
                                    File.Delete(tempPath);
                                    return;
                                }

                                if (chunkType != MsgType.DataChunk)
                                {
                                    // Could be FileDone if file is empty
                                    if (chunkType == MsgType.FileDone) break;
                                    await SendError(stream, "UNEXPECTED_MSG", "Expected DataChunk.", ct);
                                    return;
                                }

                                await cs.WriteAsync(chunkData, 0, chunkData.Length, ct);
                                fileBytesReceived += chunkData.Length;
                                received          += chunkData.Length;
                                speedometer.Add(chunkData.Length);

                                Progress?.Invoke(new TransferProgress(
                                    totalBytes, received,
                                    speedometer.BytesPerSecond,
                                    i, fileHeader.RelativePath));
                            }

                            // Finalise hash
                            await cs.FlushFinalBlockAsync(ct);
                            actualHash = BitConverter.ToString(sha256.Hash!).Replace("-", "").ToLowerInvariant();
                        }

                        // Wait for FileDone message if not already seen
                        // (already consumed above if file was zero-length)

                        bool hashMatch = string.Equals(actualHash,
                            fileHeader.Sha256Hash, StringComparison.OrdinalIgnoreCase);

                        if (hashMatch)
                        {
                            // Rename temp → final
                            if (File.Exists(destPath)) File.Delete(destPath);
                            File.Move(tempPath, destPath);
                            success = true;
                            _logger.LogInformation("File saved: {Path} (hash OK).", destPath);
                        }
                        else
                        {
                            File.Delete(tempPath);
                            _logger.LogError("Hash MISMATCH! Expected {Expected}, got {Actual}.",
                                fileHeader.Sha256Hash, actualHash);
                        }

                        // Send checksum ack
                        await stream.WriteJsonFrameAsync(MsgType.ChecksumAck,
                            new ChecksumAckMsg(i, hashMatch, actualHash), ct);

                        TransferCompleted?.Invoke(fileHeader.RelativePath, success);
                    }

                    _logger.LogInformation("Session complete from {Remote}.", remote);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling client {Remote}.", remote);
                    TransferError?.Invoke(ex.Message);
                    try { await SendError(stream, "INTERNAL_ERROR", ex.Message, ct); } catch { }
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
