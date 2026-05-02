// Networking/StreamExtensions.cs
// Async read/write helpers that enforce the 5-byte frame header protocol

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LanDrop.Networking
{
    /// <summary>
    /// Extension methods for reading and writing framed protocol messages
    /// on top of any <see cref="Stream"/> (typically a NetworkStream).
    /// </summary>
    public static class StreamExtensions
    {
        // ── Write ─────────────────────────────────────────────────────────────

        /// <summary>Write a pre-built framed message to the stream.</summary>
        public static Task WriteFrameAsync(
            this Stream stream,
            byte msgType,
            byte[] payload,
            CancellationToken ct = default)
        {
            var frame = FrameHelper.BuildFrame(msgType, payload);
            return stream.WriteAsync(frame, 0, frame.Length, ct);
        }

        /// <summary>Write a JSON-serialised framed message.</summary>
        public static Task WriteJsonFrameAsync<T>(
            this Stream stream,
            byte msgType,
            T payload,
            CancellationToken ct = default)
        {
            var frame = FrameHelper.BuildJsonFrame(msgType, payload);
            return stream.WriteAsync(frame, 0, frame.Length, ct);
        }

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Read exactly one framed message header (5 bytes) and its payload.
        /// Returns (msgType, payloadBytes).
        /// Throws <see cref="EndOfStreamException"/> on clean disconnect.
        /// </summary>
        public static async Task<(byte MsgType, byte[] Payload)> ReadFrameAsync(
            this Stream stream,
            CancellationToken ct = default)
        {
            // Read the 5-byte header
            var header = new byte[5];
            await ReadExactlyAsync(stream, header, 0, 5, ct);

            byte msgType     = header[0];
            int  payloadLen  = FrameHelper.ReadInt32BE(header, 1);

            if (payloadLen < 0 || payloadLen > 256 * 1024 * 1024) // sanity: max 256 MB JSON/control
                throw new InvalidDataException($"Invalid payload length: {payloadLen}");

            byte[] payload = new byte[payloadLen];
            if (payloadLen > 0)
                await ReadExactlyAsync(stream, payload, 0, payloadLen, ct);

            return (msgType, payload);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Read exactly <paramref name="count"/> bytes into a buffer,
        /// blocking until all bytes arrive or the stream closes.
        /// </summary>
        public static async Task ReadExactlyAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct = default)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"Connection closed after reading {totalRead}/{count} bytes.");
                totalRead += read;
            }
        }
    }
}
