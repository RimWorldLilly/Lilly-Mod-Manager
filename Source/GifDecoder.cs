using System;
using System.Collections.Generic;

namespace Lilly.IntegratedModManager
{
    /// <summary>
    /// Minimal animated GIF decoder used by the workshop preview gallery.
    /// Decoding is pure managed code so it can run off the main thread.
    /// </summary>
    internal static class GifDecoder
    {
        internal sealed class GifFrame
        {
            internal int Width;
            internal int Height;
            internal byte[] Pixels; // RGBA, row-major
            internal float Delay;
        }

        private const int MaxDictionarySize = 4096;
        private const int MaxCodeSize = 12;

        internal static bool HasGifHeader(byte[] data)
        {
            return data != null && data.Length >= 6 &&
                data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F';
        }

        internal static List<GifFrame> Decode(
            byte[] data,
            int maxFrames,
            int maxWidth,
            int maxHeight,
            long maxTotalPixels,
            out string error)
        {
            error = null;
            List<GifFrame> frames = new List<GifFrame>();
            try
            {
                DecodeCore(data, Math.Max(1, maxFrames), Math.Max(1, maxWidth),
                    Math.Max(1, maxHeight), Math.Max(1L, maxTotalPixels), frames);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                frames.Clear();
            }
            return frames;
        }

        private static void DecodeCore(
            byte[] data,
            int maxFrames,
            int maxWidth,
            int maxHeight,
            long maxTotalPixels,
            List<GifFrame> frames)
        {
            if (!HasGifHeader(data))
                throw new InvalidOperationException("Not a GIF image.");

            int position = 6;
            int screenWidth = ReadUInt16(data, ref position);
            int screenHeight = ReadUInt16(data, ref position);
            if (screenWidth <= 0 || screenHeight <= 0)
                throw new InvalidOperationException("Invalid GIF dimensions.");
            if ((long)screenWidth * screenHeight > 16000000L)
                throw new InvalidOperationException("GIF dimensions are too large.");

            int packed = ReadByte(data, ref position);
            ReadByte(data, ref position); // background color index
            ReadByte(data, ref position); // pixel aspect ratio
            byte[] globalColorTable = (packed & 0x80) != 0
                ? ReadColorTable(data, ref position, 2 << (packed & 0x07))
                : null;

            int scale = Math.Max(1, Math.Max(
                (screenWidth + maxWidth - 1) / maxWidth,
                (screenHeight + maxHeight - 1) / maxHeight));

            byte[] canvas = new byte[screenWidth * screenHeight * 4];
            int pendingDisposal = 0;
            bool pendingTransparent = false;
            int pendingTransparentIndex = -1;
            float pendingDelay = 0.1f;
            long totalPixels = 0L;

            while (position < data.Length && frames.Count < maxFrames)
            {
                int block = ReadByte(data, ref position);
                if (block == 0x3B)
                    break;

                if (block == 0x21)
                {
                    int label = ReadByte(data, ref position);
                    if (label == 0xF9)
                    {
                        int size = ReadByte(data, ref position);
                        if (size < 4)
                            throw new InvalidOperationException("Invalid GIF control block.");
                        int controlPacked = ReadByte(data, ref position);
                        int delayHundredths = ReadUInt16(data, ref position);
                        int transparentIndex = ReadByte(data, ref position);
                        for (int i = 4; i < size; i++)
                            ReadByte(data, ref position);
                        ReadByte(data, ref position); // block terminator
                        pendingDisposal = (controlPacked >> 2) & 0x07;
                        pendingTransparent = (controlPacked & 0x01) != 0;
                        pendingTransparentIndex = transparentIndex;
                        pendingDelay = delayHundredths <= 0
                            ? 0.1f
                            : delayHundredths / 100f;
                    }
                    else
                    {
                        SkipSubBlocks(data, ref position);
                    }
                    continue;
                }

                if (block != 0x2C)
                    throw new InvalidOperationException("Unexpected GIF block.");

                int left = ReadUInt16(data, ref position);
                int top = ReadUInt16(data, ref position);
                int width = ReadUInt16(data, ref position);
                int height = ReadUInt16(data, ref position);
                int imagePacked = ReadByte(data, ref position);
                byte[] colorTable = globalColorTable;
                if ((imagePacked & 0x80) != 0)
                    colorTable = ReadColorTable(data, ref position, 2 << (imagePacked & 0x07));
                if (colorTable == null)
                    throw new InvalidOperationException("GIF image has no color table.");
                bool interlaced = (imagePacked & 0x40) != 0;

                int minCodeSize = ReadByte(data, ref position);
                byte[] compressed = ReadSubBlocks(data, ref position);
                byte[] indices = DecodeLzw(compressed, minCodeSize, width * height);

                byte[] previousCanvas = pendingDisposal == 3 ? (byte[])canvas.Clone() : null;
                DrawFrame(canvas, screenWidth, screenHeight, left, top, width, height,
                    indices, colorTable, interlaced, pendingTransparent,
                    pendingTransparentIndex);

                GifFrame frame = CaptureFrame(canvas, screenWidth, screenHeight, scale,
                    pendingDelay);
                frames.Add(frame);
                totalPixels += frame.Width * (long)frame.Height;

                if (pendingDisposal == 2)
                    ClearRect(canvas, screenWidth, screenHeight, left, top, width, height);
                else if (pendingDisposal == 3 && previousCanvas != null)
                    canvas = previousCanvas;

                pendingDisposal = 0;
                pendingTransparent = false;
                pendingTransparentIndex = -1;
                pendingDelay = 0.1f;

                if (totalPixels >= maxTotalPixels)
                    break;
            }

            if (frames.Count == 0)
                throw new InvalidOperationException("GIF has no frames.");
        }

        private static void DrawFrame(
            byte[] canvas,
            int screenWidth,
            int screenHeight,
            int left,
            int top,
            int width,
            int height,
            byte[] indices,
            byte[] colorTable,
            bool interlaced,
            bool transparent,
            int transparentIndex)
        {
            if (width <= 0 || height <= 0 || indices == null)
                return;
            int colorCount = colorTable.Length / 3;
            for (int row = 0; row < height; row++)
            {
                int canvasY = top + (interlaced ? InterlacedRow(row, height) : row);
                if (canvasY < 0 || canvasY >= screenHeight)
                    continue;
                int rowOffset = row * width;
                for (int column = 0; column < width; column++)
                {
                    int canvasX = left + column;
                    if (canvasX < 0 || canvasX >= screenWidth)
                        continue;
                    int indexPosition = rowOffset + column;
                    if (indexPosition >= indices.Length)
                        return;
                    int colorIndex = indices[indexPosition];
                    if (transparent && colorIndex == transparentIndex)
                        continue;
                    if (colorIndex >= colorCount)
                        continue;
                    int source = colorIndex * 3;
                    int destination = (canvasY * screenWidth + canvasX) * 4;
                    canvas[destination] = colorTable[source];
                    canvas[destination + 1] = colorTable[source + 1];
                    canvas[destination + 2] = colorTable[source + 2];
                    canvas[destination + 3] = 255;
                }
            }
        }

        private static void ClearRect(
            byte[] canvas,
            int screenWidth,
            int screenHeight,
            int left,
            int top,
            int width,
            int height)
        {
            int xMin = Math.Max(0, left);
            int xMax = Math.Min(screenWidth, left + width);
            int yMin = Math.Max(0, top);
            int yMax = Math.Min(screenHeight, top + height);
            for (int y = yMin; y < yMax; y++)
            {
                int rowOffset = y * screenWidth;
                for (int x = xMin; x < xMax; x++)
                {
                    int offset = (rowOffset + x) * 4;
                    canvas[offset] = 0;
                    canvas[offset + 1] = 0;
                    canvas[offset + 2] = 0;
                    canvas[offset + 3] = 0;
                }
            }
        }

        private static GifFrame CaptureFrame(
            byte[] canvas,
            int screenWidth,
            int screenHeight,
            int scale,
            float delay)
        {
            int width = (screenWidth + scale - 1) / scale;
            int height = (screenHeight + scale - 1) / scale;
            byte[] pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int sourceY = Math.Min(screenHeight - 1, y * scale);
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Math.Min(screenWidth - 1, x * scale);
                    int source = (sourceY * screenWidth + sourceX) * 4;
                    int destination = (y * width + x) * 4;
                    pixels[destination] = canvas[source];
                    pixels[destination + 1] = canvas[source + 1];
                    pixels[destination + 2] = canvas[source + 2];
                    pixels[destination + 3] = canvas[source + 3];
                }
            }
            return new GifFrame
            {
                Width = width,
                Height = height,
                Pixels = pixels,
                Delay = delay
            };
        }

        private static int InterlacedRow(int row, int height)
        {
            int[] starts = { 0, 4, 2, 1 };
            int[] steps = { 8, 8, 4, 2 };
            int remaining = row;
            for (int pass = 0; pass < 4; pass++)
            {
                int count = (height - starts[pass] + steps[pass] - 1) / steps[pass];
                if (count < 0)
                    count = 0;
                if (remaining < count)
                    return starts[pass] + remaining * steps[pass];
                remaining -= count;
            }
            return Math.Max(0, height - 1);
        }

        private static byte[] DecodeLzw(byte[] data, int minCodeSize, int pixelCount)
        {
            if (minCodeSize < 1)
                minCodeSize = 1;
            if (minCodeSize > 11)
                minCodeSize = 11;
            if (pixelCount <= 0)
                return Array.Empty<byte>();

            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            int[] prefix = new int[MaxDictionarySize];
            byte[] suffix = new byte[MaxDictionarySize];
            byte[] stack = new byte[MaxDictionarySize];
            for (int i = 0; i < clearCode; i++)
            {
                prefix[i] = -1;
                suffix[i] = (byte)i;
            }

            byte[] output = new byte[pixelCount];
            int outputPosition = 0;
            int codeSize = minCodeSize + 1;
            int nextCode = endCode + 1;
            int previousCode = -1;
            byte firstCharacter = 0;
            int bitBuffer = 0;
            int bitCount = 0;
            int dataPosition = 0;

            while (outputPosition < pixelCount)
            {
                while (bitCount < codeSize)
                {
                    if (dataPosition >= data.Length)
                        return output;
                    bitBuffer |= data[dataPosition++] << bitCount;
                    bitCount += 8;
                }
                int code = bitBuffer & ((1 << codeSize) - 1);
                bitBuffer >>= codeSize;
                bitCount -= codeSize;

                if (code == clearCode)
                {
                    codeSize = minCodeSize + 1;
                    nextCode = endCode + 1;
                    previousCode = -1;
                    continue;
                }
                if (code == endCode)
                    break;

                int stackPosition = 0;
                int current;
                if (code < nextCode)
                {
                    current = code;
                }
                else
                {
                    if (previousCode < 0)
                        break;
                    stack[stackPosition++] = firstCharacter;
                    current = previousCode;
                }

                while (current > endCode && stackPosition < stack.Length)
                {
                    stack[stackPosition++] = suffix[current];
                    current = prefix[current];
                    if (current < 0)
                        break;
                }
                if (current >= 0 && current < MaxDictionarySize)
                {
                    if (stackPosition < stack.Length)
                        stack[stackPosition++] = suffix[current];
                }

                if (stackPosition > 0)
                    firstCharacter = stack[stackPosition - 1];
                while (stackPosition > 0 && outputPosition < pixelCount)
                    output[outputPosition++] = stack[--stackPosition];

                if (previousCode >= 0 && nextCode < MaxDictionarySize)
                {
                    prefix[nextCode] = previousCode;
                    suffix[nextCode] = firstCharacter;
                    nextCode++;
                    if (nextCode >= (1 << codeSize) && codeSize < MaxCodeSize)
                        codeSize++;
                }
                previousCode = code;
            }
            return output;
        }

        private static byte[] ReadColorTable(byte[] data, ref int position, int entries)
        {
            int bytes = entries * 3;
            if (position + bytes > data.Length)
                throw new InvalidOperationException("Truncated GIF color table.");
            byte[] table = new byte[bytes];
            Array.Copy(data, position, table, 0, bytes);
            position += bytes;
            return table;
        }

        private static byte[] ReadSubBlocks(byte[] data, ref int position)
        {
            List<byte> buffer = new List<byte>();
            while (position < data.Length)
            {
                int size = data[position++];
                if (size == 0)
                    break;
                if (position + size > data.Length)
                    break;
                for (int i = 0; i < size; i++)
                    buffer.Add(data[position++]);
            }
            return buffer.ToArray();
        }

        private static void SkipSubBlocks(byte[] data, ref int position)
        {
            while (position < data.Length)
            {
                int size = data[position++];
                if (size == 0)
                    return;
                position += size;
            }
        }

        private static int ReadByte(byte[] data, ref int position)
        {
            if (position >= data.Length)
                throw new InvalidOperationException("Truncated GIF data.");
            return data[position++];
        }

        private static int ReadUInt16(byte[] data, ref int position)
        {
            if (position + 1 >= data.Length)
                throw new InvalidOperationException("Truncated GIF data.");
            int value = data[position] | (data[position + 1] << 8);
            position += 2;
            return value;
        }
    }
}
