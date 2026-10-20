using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MintPet
{
    public sealed class GifFrame
    {
        public readonly Bitmap Bitmap;
        public readonly int DelayMs;

        public GifFrame(Bitmap bitmap, int delayMs)
        {
            Bitmap = bitmap;
            DelayMs = delayMs;
        }
    }

    /// <summary>
    /// A small, self-contained GIF89a reader.
    ///
    /// GDI+ (Image.FromFile / SelectActiveFrame) ignores the GIF disposal
    /// method and frame offsets, which leaves trails and smears behind on a
    /// transparent desktop pet.  This reader decodes LZW itself, composites
    /// every frame onto a transparent canvas with the proper disposal rules,
    /// and hands back finished 32bpp bitmaps.
    /// </summary>
    public sealed class GifAnimation : IDisposable
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool HasTransparency { get; private set; }
        public List<GifFrame> Frames { get; private set; }

        public GifAnimation()
        {
            Frames = new List<GifFrame>();
        }

        public int TotalDurationMs
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Frames.Count; i++) total += Frames[i].DelayMs;
                return total;
            }
        }

        public static GifAnimation FromFile(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return FromStream(fs);
            }
        }

        public static GifAnimation FromStream(Stream stream)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return Parse(ms.ToArray());
            }
        }

        private static GifAnimation Parse(byte[] data)
        {
            if (data.Length < 13) throw new InvalidDataException("文件太小，不是有效的 GIF。");
            string signature = Encoding.ASCII.GetString(data, 0, 6);
            if (!signature.StartsWith("GIF")) throw new InvalidDataException("不是 GIF 文件。");

            Reader r = new Reader(data);
            r.Position = 6;

            int screenW = r.ReadShort();
            int screenH = r.ReadShort();
            if (screenW <= 0 || screenH <= 0) throw new InvalidDataException("GIF 画布尺寸无效。");

            int packed = r.ReadByte();
            r.ReadByte();               // background colour index
            r.ReadByte();               // pixel aspect ratio

            int[] globalPalette = null;
            if ((packed & 0x80) != 0) globalPalette = ReadPalette(r, 2 << (packed & 0x07));

            GifAnimation anim = new GifAnimation();
            anim.Width = screenW;
            anim.Height = screenH;

            int[] canvas = new int[screenW * screenH];
            int delay = 0;
            int transparentIndex = -1;
            int disposal = 0;

            while (true)
            {
                int block = r.ReadByte();

                if (block == 0x3B) break;                       // trailer

                if (block == 0x21)                              // extension
                {
                    int label = r.ReadByte();
                    if (label == 0xF9)                          // graphic control
                    {
                        int size = r.ReadByte();
                        byte[] payload = r.ReadBytes(size);
                        if (size >= 4)
                        {
                            disposal = (payload[0] >> 2) & 0x07;
                            delay = payload[1] | (payload[2] << 8);
                            if ((payload[0] & 0x01) != 0)
                            {
                                transparentIndex = payload[3];
                                anim.HasTransparency = true;
                            }
                        }
                        ReadSubBlocks(r);                       // block terminator
                    }
                    else
                    {
                        ReadSubBlocks(r);
                    }
                    continue;
                }

                if (block != 0x2C) throw new InvalidDataException("GIF 数据块无法识别（0x" + block.ToString("X2") + "）。");

                // image descriptor
                int left = r.ReadShort();
                int top = r.ReadShort();
                int imageW = r.ReadShort();
                int imageH = r.ReadShort();
                if (imageW <= 0 || imageH <= 0) throw new InvalidDataException("GIF 帧尺寸无效。");

                int imagePacked = r.ReadByte();
                int[] localPalette = null;
                if ((imagePacked & 0x80) != 0) localPalette = ReadPalette(r, 2 << (imagePacked & 0x07));
                bool interlaced = (imagePacked & 0x40) != 0;

                int minCodeSize = r.ReadByte();
                byte[] compressed = ReadSubBlocks(r);
                int[] palette = localPalette ?? globalPalette;
                if (palette == null) throw new InvalidDataException("GIF 缺少调色板。");

                byte[] indices = LzwDecode(compressed, minCodeSize, imageW * imageH);

                int[] before = disposal == 3 ? (int[])canvas.Clone() : null;

                DrawFrame(canvas, screenW, screenH, palette, indices,
                          left, top, imageW, imageH, interlaced, transparentIndex);

                int frameDelay = delay <= 0 ? 100 : delay * 10;
                if (frameDelay < 10) frameDelay = 10;
                anim.Frames.Add(new GifFrame(ToBitmap(canvas, screenW, screenH), frameDelay));

                if (disposal == 2) ClearRect(canvas, screenW, screenH, left, top, imageW, imageH);
                else if (disposal == 3 && before != null) canvas = before;

                delay = 0;
                transparentIndex = -1;
                disposal = 0;
            }

            if (anim.Frames.Count == 0) throw new InvalidDataException("GIF 里没有可播放的帧。");
            return anim;
        }

        private static void DrawFrame(int[] canvas, int canvasW, int canvasH, int[] palette,
                                      byte[] indices, int left, int top, int imageW, int imageH,
                                      bool interlaced, int transparentIndex)
        {
            int[] rowOrder = new int[imageH];
            if (interlaced)
            {
                int[] starts = { 0, 4, 2, 1 };
                int[] steps = { 8, 8, 4, 2 };
                int k = 0;
                for (int p = 0; p < 4; p++)
                {
                    for (int y = starts[p]; y < imageH; y += steps[p]) rowOrder[k++] = y;
                }
            }
            else
            {
                for (int y = 0; y < imageH; y++) rowOrder[y] = y;
            }

            for (int srcY = 0; srcY < imageH; srcY++)
            {
                int destY = top + rowOrder[srcY];
                if (destY < 0 || destY >= canvasH) continue;
                int srcRow = srcY * imageW;
                int destRow = destY * canvasW;

                for (int x = 0; x < imageW; x++)
                {
                    int destX = left + x;
                    if (destX < 0 || destX >= canvasW) continue;
                    int src = srcRow + x;
                    if (src >= indices.Length) break;

                    int index = indices[src];
                    if (index == transparentIndex) continue;
                    if (index >= palette.Length) continue;

                    canvas[destRow + destX] = palette[index];
                }
            }
        }

        private static void ClearRect(int[] canvas, int canvasW, int canvasH, int left, int top, int w, int h)
        {
            for (int y = top; y < top + h; y++)
            {
                if (y < 0 || y >= canvasH) continue;
                int row = y * canvasW;
                for (int x = left; x < left + w; x++)
                {
                    if (x < 0 || x >= canvasW) continue;
                    canvas[row + x] = 0;
                }
            }
        }

        private static Bitmap ToBitmap(int[] pixels, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, width, height),
                                              ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length);
            bitmap.UnlockBits(bits);
            return bitmap;
        }

        private static int[] ReadPalette(Reader r, int count)
        {
            int[] palette = new int[count];
            for (int i = 0; i < count; i++)
            {
                int red = r.ReadByte();
                int green = r.ReadByte();
                int blue = r.ReadByte();
                palette[i] = unchecked((int)0xFF000000) | (red << 16) | (green << 8) | blue;
            }
            return palette;
        }

        private static byte[] ReadSubBlocks(Reader r)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                while (true)
                {
                    int size = r.ReadByte();
                    if (size == 0) break;
                    byte[] chunk = r.ReadBytes(size);
                    ms.Write(chunk, 0, chunk.Length);
                }
                return ms.ToArray();
            }
        }

        private static byte[] LzwDecode(byte[] data, int minCodeSize, int expected)
        {
            if (minCodeSize < 2 || minCodeSize > 11) throw new InvalidDataException("GIF LZW 参数无效。");

            int clearCode = 1 << minCodeSize;
            int endCode = clearCode + 1;
            int codeSize = minCodeSize + 1;
            int nextCode = clearCode + 2;

            int[] prefix = new int[4096];
            byte[] suffix = new byte[4096];
            byte[] stack = new byte[4096];
            for (int i = 0; i < clearCode; i++)
            {
                prefix[i] = -1;
                suffix[i] = (byte)i;
            }

            List<byte> output = new List<byte>(expected);
            int oldCode = -1;
            int firstChar = 0;
            int bitBuffer = 0;
            int bitCount = 0;
            int position = 0;

            while (true)
            {
                while (bitCount < codeSize)
                {
                    if (position >= data.Length) return output.ToArray();
                    bitBuffer |= data[position++] << bitCount;
                    bitCount += 8;
                }

                int code = bitBuffer & ((1 << codeSize) - 1);
                bitBuffer >>= codeSize;
                bitCount -= codeSize;

                if (code == clearCode)
                {
                    codeSize = minCodeSize + 1;
                    nextCode = clearCode + 2;
                    oldCode = -1;
                    continue;
                }
                if (code == endCode) break;

                if (oldCode == -1)
                {
                    if (code >= clearCode) throw new InvalidDataException("GIF LZW 数据损坏。");
                    output.Add(suffix[code]);
                    firstChar = suffix[code];
                    oldCode = code;
                    continue;
                }

                int current = code;
                int stackSize = 0;

                if (code >= nextCode)
                {
                    if (code > nextCode) throw new InvalidDataException("GIF LZW 字典越界。");
                    stack[stackSize++] = (byte)firstChar;    // KwKwK case
                    current = oldCode;
                }

                while (current >= clearCode)
                {
                    if (stackSize >= stack.Length) throw new InvalidDataException("GIF LZW 数据损坏。");
                    stack[stackSize++] = suffix[current];
                    current = prefix[current];
                    if (current < 0) throw new InvalidDataException("GIF LZW 数据损坏。");
                }

                stack[stackSize++] = suffix[current];
                firstChar = stack[stackSize - 1];

                while (stackSize > 0) output.Add(stack[--stackSize]);

                if (nextCode < 4096)
                {
                    prefix[nextCode] = oldCode;
                    suffix[nextCode] = (byte)firstChar;
                    nextCode++;
                    if (nextCode == (1 << codeSize) && codeSize < 12) codeSize++;
                }

                oldCode = code;

                if (output.Count >= expected * 2) break;
            }

            if (output.Count > expected) output.RemoveRange(expected, output.Count - expected);
            return output.ToArray();
        }

        public void Dispose()
        {
            for (int i = 0; i < Frames.Count; i++)
            {
                if (Frames[i].Bitmap != null) Frames[i].Bitmap.Dispose();
            }
            Frames.Clear();
        }

        private sealed class Reader
        {
            private readonly byte[] data;
            public int Position;

            public Reader(byte[] bytes)
            {
                data = bytes;
            }

            public int ReadByte()
            {
                if (Position >= data.Length) throw new InvalidDataException("GIF 数据意外结束。");
                return data[Position++];
            }

            public int ReadShort()
            {
                int low = ReadByte();
                int high = ReadByte();
                return low | (high << 8);
            }

            public byte[] ReadBytes(int count)
            {
                if (count < 0) throw new InvalidDataException("GIF 数据长度无效。");
                if (Position + count > data.Length) throw new InvalidDataException("GIF 数据意外结束。");
                byte[] result = new byte[count];
                Array.Copy(data, Position, result, 0, count);
                Position += count;
                return result;
            }
        }
    }
}
