using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using LanDrop.Models;
using LanDrop.Networking;
using Microsoft.Extensions.Logging.Abstractions;

var checks = new (string Name, Func<Task> Run)[]
{
    ("frame lengths reject oversized and empty data before allocation", CheckFrameLimits),
    ("first-use hotspot credentials are unpredictable", CheckCredentials),
    ("duplicate received names preserve all files", CheckDuplicateNames),
    ("compressed file transfers preserve content", CheckCompressedTransfer),
    ("receive cancellation removes partial files", CheckCancellation),
    ("oversized decompressed chunks fail without saving", CheckDecompressedLimit),
    ("only one incoming connection runs at a time", CheckConnectionLimit),
    ("accepted idle pauses can resume after the read timeout", CheckAcceptedIdlePause),
    ("announced payloads still time out", CheckPayloadTimeout),
    ("stalled handshakes time out", CheckReadTimeout)
};
foreach (var check in checks)
{
    await check.Run();
    Console.WriteLine($"PASS {check.Name}");
}
Console.WriteLine($"{checks.Length} checks passed.");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task CheckFrameLimits()
{
    foreach (var (type, length) in new[]
    {
        (MsgType.Hello, 64 * 1024 + 1), (MsgType.PinResponse, 129),
        (MsgType.DataChunk, StreamExtensions.MaxChunkSizeBytes + 1), (MsgType.DataChunk, 0),
        (MsgType.CompressedChunk, StreamExtensions.MaxChunkSizeBytes + 5),
        (MsgType.CompressedChunk, 4), (MsgType.Hello, -1)
    })
    {
        byte[] header = new byte[5];
        header[0] = type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), length);
        using var stream = new MemoryStream(header);
        try
        {
            await stream.ReadHeaderAsync();
            throw new InvalidOperationException($"Accepted invalid frame length {length}.");
        }
        catch (InvalidDataException) { }
    }
    using var data = new MemoryStream(FrameHelper.BuildFrame(MsgType.DataChunk, new byte[] { 1 }));
    try
    {
        await data.ReadFrameAsync();
        throw new InvalidOperationException("Accepted data before the control handshake.");
    }
    catch (InvalidDataException) { }
    using var hello = new MemoryStream(FrameHelper.BuildJsonFrame(MsgType.Hello,
        new HelloMsg("test", "test", "2", 0, 0, false)));
    var frame = await hello.ReadFrameAsync();
    Assert(frame.MsgType == MsgType.Hello, "Valid Hello was rejected.");
}

static Task CheckCredentials()
{
    var passwords = Enumerable.Range(0, 100).Select(_ => new AppSettings().WifiDirectPass).ToArray();
    Assert(passwords.All(p => p.Length == 24 && p != "11111111"), "Invalid generated hotspot password.");
    Assert(passwords.Distinct().Count() == passwords.Length, "Repeated generated hotspot password.");
    return Task.CompletedTask;
}

static async Task<TcpClient> Connect(ReceiverFixture fixture, int files, long bytes)
{
    var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, fixture.Port);
    var stream = client.GetStream();
    await stream.WriteJsonFrameAsync(MsgType.Hello, new HelloMsg("test", "test", "2", files, bytes, false));
    var ack = await stream.ReadFrameAsync();
    Assert(ack.MsgType == MsgType.HelloAck && FrameHelper.FromJson<HelloAckMsg>(ack.Payload).Accepted,
        "Transfer was not accepted.");
    return client;
}

static async Task SendFile(NetworkStream stream, int index, string name, byte[] content)
{
    await stream.WriteJsonFrameAsync(MsgType.FileHeader, new FileHeaderMsg(index, name, content.Length));
    if (content.Length > 0)
        await stream.WriteFrameAsync(MsgType.DataChunk, content);
    var hash = new FastHasher();
    hash.Append(content);
    await stream.WriteJsonFrameAsync(MsgType.FileDone, new FileDoneMsg(index, content.Length, hash.GetHashHex()));
    var result = await stream.ReadFrameAsync();
    Assert(result.MsgType == MsgType.ChecksumAck && FrameHelper.FromJson<ChecksumAckMsg>(result.Payload).HashMatch,
        "File verification failed.");
}

static async Task CheckDuplicateNames()
{
    using var fixture = new ReceiverFixture();
    var original = new byte[] { 9, 8, 7 };
    var savedNames = new List<string>();
    fixture.Receiver.TransferCompleted += (path, _, success) => { if (success) savedNames.Add(path); };
    await File.WriteAllBytesAsync(Path.Combine(fixture.Directory, "report.pdf"), original);
    using var client = await Connect(fixture, 3, 6);
    await SendFile(client.GetStream(), 0, "report.pdf", new byte[] { 1, 2, 3 });
    await SendFile(client.GetStream(), 1, "report.pdf", new byte[] { 4, 5, 6 });
    await SendFile(client.GetStream(), 2, "empty.txt", Array.Empty<byte>());
    await fixture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(File.ReadAllBytes(Path.Combine(fixture.Directory, "report.pdf")).SequenceEqual(original),
        "Existing file was overwritten.");
    Assert(File.ReadAllBytes(Path.Combine(fixture.Directory, "report (1).pdf")).SequenceEqual(new byte[] { 1, 2, 3 }),
        "First duplicate did not get its own filename.");
    Assert(File.ReadAllBytes(Path.Combine(fixture.Directory, "report (2).pdf")).SequenceEqual(new byte[] { 4, 5, 6 }),
        "Second duplicate did not get its own filename.");
    Assert(new FileInfo(Path.Combine(fixture.Directory, "empty.txt")).Length == 0, "Empty file failed.");
    Assert(savedNames.SequenceEqual(new[] { "report (1).pdf", "report (2).pdf", "empty.txt" }),
        "Receiver did not report the actual saved filenames.");
    AssertNoPartialFiles(fixture);
}

static async Task CheckCancellation()
{
    using var fixture = new ReceiverFixture();
    var progress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    fixture.Receiver.Progress += _ => progress.TrySetResult();
    using var client = await Connect(fixture, 1, 1024);
    await client.GetStream().WriteJsonFrameAsync(MsgType.FileHeader, new FileHeaderMsg(0, "partial.txt", 1024));
    await client.GetStream().WriteFrameAsync(MsgType.DataChunk, new byte[16]);
    await progress.Task.WaitAsync(TimeSpan.FromSeconds(5));
    fixture.Receiver.Pause();
    fixture.Receiver.CancelTransfer();
    await fixture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!File.Exists(Path.Combine(fixture.Directory, "partial.txt")), "Cancelled transfer saved a partial file.");
    AssertNoPartialFiles(fixture);
}

static async Task CheckCompressedTransfer()
{
    using var fixture = new ReceiverFixture();
    byte[] content = Enumerable.Repeat((byte)65, 8192).ToArray();
    byte[] compressed = new byte[content.Length];
    int compressedLength = FastCompress.TryCompress(content, compressed);
    Assert(compressedLength > 0, "Compressible input was not compressed.");
    using var client = await Connect(fixture, 1, content.Length);
    var stream = client.GetStream();
    await stream.WriteJsonFrameAsync(MsgType.FileHeader, new FileHeaderMsg(0, "nested/compressed.txt", content.Length));
    await stream.WriteCompressedChunkFrameAsync(compressed, 0, compressedLength, content.Length);
    var hash = new FastHasher();
    hash.Append(content);
    await stream.WriteJsonFrameAsync(MsgType.FileDone, new FileDoneMsg(0, content.Length, hash.GetHashHex()));
    var ack = await stream.ReadFrameAsync();
    Assert(ack.MsgType == MsgType.ChecksumAck && FrameHelper.FromJson<ChecksumAckMsg>(ack.Payload).HashMatch,
        "Compressed transfer checksum failed.");
    await fixture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(File.ReadAllBytes(Path.Combine(fixture.Directory, "nested", "compressed.txt")).SequenceEqual(content),
        "Compressed transfer changed file contents.");
    AssertNoPartialFiles(fixture);
}

static async Task CheckDecompressedLimit()
{
    using var fixture = new ReceiverFixture();
    using var client = await Connect(fixture, 1, 10_000_000);
    await client.GetStream().WriteJsonFrameAsync(MsgType.FileHeader, new FileHeaderMsg(0, "invalid.txt", 10_000_000));
    var payload = new byte[5];
    BinaryPrimitives.WriteInt32BigEndian(payload, 10_000_000);
    await client.GetStream().WriteFrameAsync(MsgType.CompressedChunk, payload);
    var error = await fixture.Error.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(error.Contains("Invalid compressed lengths"), "Oversized decompression did not fail at its length check.");
    await fixture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(!File.Exists(Path.Combine(fixture.Directory, "invalid.txt")), "Invalid compressed transfer saved a file.");
    AssertNoPartialFiles(fixture);
}

static async Task CheckConnectionLimit()
{
    using var fixture = new ReceiverFixture();
    using var first = await Connect(fixture, 1, 1);
    using var second = new TcpClient();
    await second.ConnectAsync(IPAddress.Loopback, fixture.Port);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    int count = await second.GetStream().ReadAsync(new byte[1], timeout.Token);
    Assert(count == 0, "Second incoming connection was not closed.");
    fixture.Receiver.CancelTransfer();
    await fixture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
}

static async Task CheckReadTimeout()
{
    using var fixture = new ReceiverFixture(TimeSpan.FromMilliseconds(200));
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, fixture.Port);
    var error = await fixture.Error.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(error.Contains("within 0.2 seconds"), "Stalled handshake did not report its read timeout.");
}

static async Task CheckAcceptedIdlePause()
{
    using var fixture = new ReceiverFixture(TimeSpan.FromMilliseconds(200));
    using var client = await Connect(fixture, 1, 1);
    var stream = client.GetStream();
    await Task.Delay(500);
    await stream.WriteJsonFrameAsync(MsgType.FileHeader, new FileHeaderMsg(0, "paused.txt", 1));
    await Task.Delay(500);
    await stream.WriteFrameAsync(MsgType.DataChunk, new byte[] { 42 });
    await Task.Delay(500);
    var hash = new FastHasher();
    hash.Append(new byte[] { 42 });
    await stream.WriteJsonFrameAsync(MsgType.FileDone, new FileDoneMsg(0, 1, hash.GetHashHex()));
    var ack = await stream.ReadFrameAsync();
    Assert(ack.MsgType == MsgType.ChecksumAck && FrameHelper.FromJson<ChecksumAckMsg>(ack.Payload).HashMatch,
        "Accepted idle pause interrupted the transfer.");
    await fixture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(File.ReadAllBytes(Path.Combine(fixture.Directory, "paused.txt")).SequenceEqual(new byte[] { 42 }),
        "Resuming an accepted idle pause changed file contents.");
}

static async Task CheckPayloadTimeout()
{
    using var fixture = new ReceiverFixture(TimeSpan.FromMilliseconds(200));
    using var client = await Connect(fixture, 1, 1);
    var header = new byte[5];
    header[0] = MsgType.FileHeader;
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1), 10);
    await client.GetStream().WriteAsync(header);
    var error = await fixture.Error.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Assert(error.Contains("within 0.2 seconds"), "Announced control payload did not time out.");
    await fixture.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    AssertNoPartialFiles(fixture);
}

static void AssertNoPartialFiles(ReceiverFixture fixture) =>
    Assert(!System.IO.Directory.EnumerateFiles(fixture.Directory, "*.landrop_tmp", SearchOption.AllDirectories).Any(),
        "Temporary receive file was not cleaned up.");

sealed class ReceiverFixture : IDisposable
{
    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "LanDrop-check-" + Guid.NewGuid());
    public int Port { get; }
    public FileReceiver Receiver { get; }
    public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<string> Error { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ReceiverFixture(TimeSpan? readTimeout = null)
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        Port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        System.IO.Directory.CreateDirectory(Directory);
        Receiver = new FileReceiver(new AppSettings { TransferPort = Port, ReceiveSavePath = Directory },
            NullLogger<FileReceiver>.Instance)
        {
            NetworkReadTimeout = readTimeout ?? TimeSpan.FromSeconds(30)
        };
        Receiver.SessionCompleted += () => Completed.TrySetResult();
        Receiver.TransferError += message => Error.TrySetResult(message);
        Receiver.StartListening();
    }

    public void Dispose()
    {
        Receiver.Dispose();
        System.IO.Directory.Delete(Directory, true);
    }
}
