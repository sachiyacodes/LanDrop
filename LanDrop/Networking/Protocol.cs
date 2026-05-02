// Networking/Protocol.cs
// Wire-level constants and message types shared by sender and receiver

using System;
using System.Text;
using System.Text.Json;

namespace LanDrop.Networking
{
    /// <summary>
    /// Single-byte message type identifiers sent over TCP.
    /// Every message on the wire is:   [1-byte type][4-byte payload-length][payload bytes]
    /// </summary>
    public static class MsgType
    {
        // ── Handshake ────────────────────────────────────────────────────────
        public const byte Hello        = 0x01;   // sender → receiver: session metadata JSON
        public const byte HelloAck     = 0x02;   // receiver → sender: accept / deny JSON
        public const byte PinChallenge = 0x03;   // receiver → sender: "please send PIN"
        public const byte PinResponse  = 0x04;   // sender → receiver: PIN string

        // ── File header ──────────────────────────────────────────────────────
        public const byte FileHeader   = 0x10;   // sender → receiver: FileHeaderMsg JSON

        // ── Data ─────────────────────────────────────────────────────────────
        public const byte DataChunk    = 0x20;   // sender → receiver: raw bytes

        // ── Control ──────────────────────────────────────────────────────────
        public const byte Pause        = 0x30;
        public const byte Resume       = 0x31;
        public const byte Cancel       = 0x32;
        public const byte FileDone     = 0x33;   // sender signals end of one file
        public const byte SessionDone  = 0x34;   // sender signals all files done

        // ── Verification ─────────────────────────────────────────────────────
        public const byte ChecksumReq  = 0x40;   // sender: "here is my SHA-256"
        public const byte ChecksumAck  = 0x41;   // receiver: "match / mismatch"

        // ── Errors ───────────────────────────────────────────────────────────
        public const byte Error        = 0xFF;
    }

    // ── JSON payload types ────────────────────────────────────────────────────

    /// <summary>Sent by sender in the Hello message.</summary>
    public record HelloMsg(
        string SessionId,
        string SenderName,
        string AppVersion,
        int    FileCount,
        long   TotalBytes,
        bool   RequiresPin
    );

    /// <summary>Sent by receiver in response to Hello.</summary>
    public record HelloAckMsg(
        bool   Accepted,
        string Reason,       // populated if Accepted == false
        string SavePath      // informational; actual save path decided by receiver
    );

    /// <summary>Sent immediately before each file's data chunks.</summary>
    public record FileHeaderMsg(
        int    FileIndex,
        string RelativePath,
        long   SizeBytes,
        string Sha256Hash
    );

    /// <summary>Sent by sender after all chunks for a file.</summary>
    public record FileDoneMsg(int FileIndex);

    /// <summary>Sent by receiver after verifying the file hash.</summary>
    public record ChecksumAckMsg(int FileIndex, bool HashMatch, string? ActualHash);

    /// <summary>Generic error payload.</summary>
    public record ErrorMsg(string Code, string Message);

    // ── Framing helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Helpers for reading/writing framed messages.
    /// Frame:  [1-byte msgType] [4-byte big-endian payloadLength] [payload]
    /// </summary>
    public static class FrameHelper
    {
        private static readonly JsonSerializerOptions _jsonOpts =
            new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        /// <summary>Serialise an object to JSON bytes.</summary>
        public static byte[] ToJson<T>(T obj) =>
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj, _jsonOpts));

        /// <summary>Deserialise JSON bytes to the given type.</summary>
        public static T FromJson<T>(byte[] data) =>
            JsonSerializer.Deserialize<T>(data, _jsonOpts)!;

        /// <summary>Build a framed message ready to write to the stream.</summary>
        public static byte[] BuildFrame(byte msgType, byte[] payload)
        {
            var frame = new byte[5 + payload.Length];
            frame[0] = msgType;
            WriteInt32BE(frame, 1, payload.Length);
            Buffer.BlockCopy(payload, 0, frame, 5, payload.Length);
            return frame;
        }

        /// <summary>Build a framed message with a JSON-serialised payload.</summary>
        public static byte[] BuildJsonFrame<T>(byte msgType, T payload) =>
            BuildFrame(msgType, ToJson(payload));

        private static void WriteInt32BE(byte[] buf, int offset, int value)
        {
            buf[offset + 0] = (byte)((value >> 24) & 0xFF);
            buf[offset + 1] = (byte)((value >> 16) & 0xFF);
            buf[offset + 2] = (byte)((value >>  8) & 0xFF);
            buf[offset + 3] = (byte)( value        & 0xFF);
        }

        public static int ReadInt32BE(byte[] buf, int offset) =>
            (buf[offset] << 24) | (buf[offset + 1] << 16) |
            (buf[offset + 2] << 8) | buf[offset + 3];
    }
}
