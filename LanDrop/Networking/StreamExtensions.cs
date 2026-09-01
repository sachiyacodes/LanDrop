// Networking/StreamExtensions.cs
// Async read/write helpers that enforce the 5-byte frame header protocol with zero-allocation pooling

using System;
using System.Buffers;
using System.Buffers.Binary;
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
        public static async Task WriteFrameAsync(
            this Stream stream,
            byte msgType,
            byte[]? payload,
            CancellationToken ct = default)
        {
            var frame = FrameHelper.BuildFrame(msgType, payload ?? Array.Empty<byte>());
            await stream.WriteAsync(frame, 0, frame.Length, ct);
        }

        /// <summary>
        /// Write a data chunk frame directly from an existing buffer with zero memory allocations.
        /// </summary>
        public static async Task WriteChunkFrameAsync(
            this Stream stream,
            byte msgType,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken ct = default)
        {
            byte[] header = ArrayPool<byte>.Shared.Rent(5);
            try
            {
                header[0] = msgType;
                BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), count);
                await stream.WriteAsync(header.AsMemory(0, 5), ct);
                if (count > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(offset, count), ct);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(header);
            }
        }

        /// <summary>
        /// Write a compressed data chunk frame with uncompressed length header and zero allocations.
        /// </summary>
        public static async Task WriteCompressedChunkFrameAsync(
            this Stream stream,
            byte[] compressedBuffer,
            int compressedOffset,
            int compressedCount,
            int uncompressedLen,
            CancellationToken ct = default)
        {
            byte[] header = ArrayPool<byte>.Shared.Rent(9);
            try
            {
                header[0] = MsgType.CompressedChunk;
                BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), compressedCount + 4);
                BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(5, 4), uncompressedLen);
                await stream.WriteAsync(header.AsMemory(0, 9), ct);
                if (compressedCount > 0)
                {
                    await stream.WriteAsync(compressedBuffer.AsMemory(compressedOffset, compressedCount), ct);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(header);
            }
        }

        /// <summary>Write a JSON-serialised framed message.</summary>
        public static async Task WriteJsonFrameAsync<T>(
            this Stream stream,
            byte msgType,
            T payload,
            CancellationToken ct = default)
        {
            var frame = FrameHelper.BuildJsonFrame(msgType, payload);
            await stream.WriteAsync(frame, 0, frame.Length, ct);
        }

        // ── Read ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Read exactly one 5-byte frame header (msgType, payloadLen).
        /// </summary>
        public static async Task<(byte MsgType, int PayloadLen)> ReadHeaderAsync(
            this Stream stream,
            CancellationToken ct = default)
        {
            byte[] header = ArrayPool<byte>.Shared.Rent(5);
            try
            {
                await ReadExactlyAsync(stream, header, 0, 5, ct);
                byte msgType = header[0];
                int payloadLen = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));

                if (payloadLen < 0 || payloadLen > 256 * 1024 * 1024)
                    throw new InvalidDataException($"Invalid payload length: {payloadLen}");

                return (msgType, payloadLen);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(header);
            }
        }

        /// <summary>
        /// Read exactly one framed message header (5 bytes) and its payload.
        /// Returns (msgType, payloadBytes).
        /// Throws <see cref="EndOfStreamException"/> on clean disconnect or premature EOF.
        /// </summary>
        public static async Task<(byte MsgType, byte[] Payload)> ReadFrameAsync(
            this Stream stream,
            CancellationToken ct = default)
        {
            var (msgType, payloadLen) = await ReadHeaderAsync(stream, ct);
            byte[] payload = payloadLen > 0 ? new byte[payloadLen] : Array.Empty<byte>();
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
            if (count == 0) return;
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
                if (read == 0)
                {
                    if (totalRead == 0)
                        throw new EndOfStreamException("Connection closed by remote peer.");
                    throw new EndOfStreamException(
                        $"Connection closed prematurely after reading {totalRead}/{count} bytes.");
                }
                totalRead += read;
            }
        }
    }
}
