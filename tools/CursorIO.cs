using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Reader/writer for the package encoding: every image is a 32-bit straight-alpha
// BGRA DIB followed by a 1-bit AND mask, exactly as the v3 files were produced.
namespace CursorForge
{
    internal sealed class Img
    {
        public readonly int W;
        public readonly int H;
        public readonly byte[] P; // straight BGRA, top-down

        public Img(int w, int h) { W = w; H = h; P = new byte[w * h * 4]; }

        public Img Clone()
        {
            Img c = new Img(W, H);
            Buffer.BlockCopy(P, 0, c.P, 0, P.Length);
            return c;
        }

        public int A(int x, int y) { return (x < 0 || y < 0 || x >= W || y >= H) ? 0 : P[(y * W + x) * 4 + 3]; }

        public static Img FromBitmap(Bitmap source)
        {
            Img img = new Img(source.Width, source.Height);
            using (Bitmap copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(copy))
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    g.DrawImage(source, 0, 0, source.Width, source.Height);
                }
                BitmapData data = copy.LockBits(new Rectangle(0, 0, img.W, img.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < img.H; y++)
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), img.P, y * img.W * 4, img.W * 4);
                }
                finally { copy.UnlockBits(data); }
            }
            return img;
        }

        public static Img LoadPng(string path)
        {
            using (Bitmap b = new Bitmap(path)) return FromBitmap(b);
        }

        public Bitmap ToBitmap()
        {
            Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            BitmapData data = b.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < H; y++)
                    Marshal.Copy(P, y * W * 4, IntPtr.Add(data.Scan0, y * data.Stride), W * 4);
            }
            finally { b.UnlockBits(data); }
            return b;
        }

        public void SavePng(string path)
        {
            using (Bitmap b = ToBitmap()) b.Save(path, ImageFormat.Png);
        }
    }

    internal sealed class CurImage
    {
        public int HotX;
        public int HotY;
        public Img Pixels;
        public int Size { get { return Pixels.W; } }
    }

    internal sealed class CurFile
    {
        public readonly List<CurImage> Images = new List<CurImage>();

        public CurImage BySize(int size)
        {
            foreach (CurImage i in Images) if (i.Size == size) return i;
            throw new InvalidDataException("No " + size + "px image in cursor.");
        }

        public static CurFile Read(string path) { return Parse(File.ReadAllBytes(path), 0); }

        public static CurFile Parse(byte[] b, int start)
        {
            if (BitConverter.ToUInt16(b, start) != 0 || BitConverter.ToUInt16(b, start + 2) != 2)
                throw new InvalidDataException("Not a cursor resource.");
            int count = BitConverter.ToUInt16(b, start + 4);
            CurFile cur = new CurFile();
            for (int i = 0; i < count; i++)
            {
                int e = start + 6 + i * 16;
                int w = b[e] == 0 ? 256 : b[e];
                int offset = start + BitConverter.ToInt32(b, e + 12);
                CurImage image = new CurImage();
                image.HotX = BitConverter.ToUInt16(b, e + 4);
                image.HotY = BitConverter.ToUInt16(b, e + 6);
                image.Pixels = ReadDib(b, offset, w);
                cur.Images.Add(image);
            }
            return cur;
        }

        private static Img ReadDib(byte[] b, int offset, int size)
        {
            int headerSize = BitConverter.ToInt32(b, offset);
            int w = BitConverter.ToInt32(b, offset + 4);
            int bits = BitConverter.ToUInt16(b, offset + 14);
            if (bits != 32 || w != size) throw new InvalidDataException("Only 32-bit square DIB cursors are supported.");
            Img img = new Img(w, w);
            int px = offset + headerSize;
            for (int y = 0; y < w; y++)
                Buffer.BlockCopy(b, px + (w - 1 - y) * w * 4, img.P, y * w * 4, w * 4);
            return img;
        }

        public byte[] ToBytes()
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter wr = new BinaryWriter(ms))
            {
                wr.Write((ushort)0); wr.Write((ushort)2); wr.Write((ushort)Images.Count);
                int offset = 6 + 16 * Images.Count;
                List<byte[]> blobs = new List<byte[]>();
                foreach (CurImage image in Images)
                {
                    byte[] dib = WriteDib(image.Pixels);
                    blobs.Add(dib);
                    wr.Write((byte)(image.Size >= 256 ? 0 : image.Size));
                    wr.Write((byte)(image.Size >= 256 ? 0 : image.Size));
                    wr.Write((byte)0); wr.Write((byte)0);
                    wr.Write((ushort)image.HotX); wr.Write((ushort)image.HotY);
                    wr.Write(dib.Length); wr.Write(offset);
                    offset += dib.Length;
                }
                foreach (byte[] blob in blobs) wr.Write(blob);
                wr.Flush();
                return ms.ToArray();
            }
        }

        internal static byte[] WriteDib(Img img)
        {
            int w = img.W;
            int maskStride = ((w + 31) / 32) * 4;
            byte[] dib = new byte[40 + w * w * 4 + maskStride * w];
            BitConverter.GetBytes(40).CopyTo(dib, 0);
            BitConverter.GetBytes(w).CopyTo(dib, 4);
            BitConverter.GetBytes(w * 2).CopyTo(dib, 8);
            BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
            BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
            BitConverter.GetBytes(w * w * 4 + maskStride * w).CopyTo(dib, 20);
            for (int y = 0; y < w; y++)
                Buffer.BlockCopy(img.P, y * w * 4, dib, 40 + (w - 1 - y) * w * 4, w * 4);
            int mask = 40 + w * w * 4;
            for (int y = 0; y < w; y++)
            {
                int row = mask + (w - 1 - y) * maskStride;
                for (int x = 0; x < w; x++)
                    if (img.P[(y * w + x) * 4 + 3] == 0) dib[row + x / 8] |= (byte)(0x80 >> (x % 8));
            }
            return dib;
        }

        public void Save(string path) { File.WriteAllBytes(path, ToBytes()); }
    }

    internal sealed class AniFile
    {
        public readonly List<CurFile> Frames = new List<CurFile>();
        public int Jiffies = 4; // default 1/60 s per step
        // Optional step table. When null, each frame is shown once in order at Jiffies.
        // A sequence lets frames repeat (a symmetric pulse stores only half its frames),
        // and per-step rates let a resting pose hold without storing duplicate frames.
        public List<int> Sequence;
        public List<int> Rates;

        public int StepCount { get { return Sequence != null ? Sequence.Count : Rates != null ? Rates.Count : Frames.Count; } }
        public int FrameAt(int step) { return Sequence != null ? Sequence[step] : step; }
        public int RateAt(int step) { return Rates != null ? Rates[step] : Jiffies; }
        public int TotalJiffies { get { int t = 0; for (int i = 0; i < StepCount; i++) t += RateAt(i); return t; } }

        public static AniFile Read(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            Encoding ascii = Encoding.ASCII;
            if (ascii.GetString(b, 0, 4) != "RIFF" || ascii.GetString(b, 8, 4) != "ACON") throw new InvalidDataException("Not an animated cursor.");
            AniFile ani = new AniFile();
            int p = 12;
            while (p + 8 <= b.Length)
            {
                string id = ascii.GetString(b, p, 4);
                int len = BitConverter.ToInt32(b, p + 4);
                if (id == "anih") ani.Jiffies = BitConverter.ToInt32(b, p + 8 + 28);
                else if (id == "rate" || id == "seq ")
                {
                    List<int> values = new List<int>();
                    for (int i = 0; i < len / 4; i++) values.Add(BitConverter.ToInt32(b, p + 8 + i * 4));
                    if (id == "rate") ani.Rates = values; else ani.Sequence = values;
                }
                else if (id == "LIST" && ascii.GetString(b, p + 8, 4) == "fram")
                {
                    int q = p + 12;
                    while (q < p + 8 + len)
                    {
                        int sub = BitConverter.ToInt32(b, q + 4);
                        if (ascii.GetString(b, q, 4) == "icon") ani.Frames.Add(CurFile.Parse(b, q + 8));
                        q += 8 + sub + (sub & 1);
                    }
                }
                p += 8 + len + (len & 1);
            }
            return ani;
        }

        public byte[] ToBytes()
        {
            List<byte[]> icons = new List<byte[]>();
            foreach (CurFile f in Frames) icons.Add(f.ToBytes());
            if (Sequence != null && Rates != null && Sequence.Count != Rates.Count) throw new InvalidDataException("Sequence and rate tables differ in length.");
            if (Sequence != null) foreach (int f in Sequence) if (f < 0 || f >= Frames.Count) throw new InvalidDataException("Sequence refers to a missing frame.");
            bool table = Sequence != null || Rates != null;
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter wr = new BinaryWriter(ms))
            {
                Encoding ascii = Encoding.ASCII;
                int listLen = 4;
                foreach (byte[] icon in icons) listLen += 8 + icon.Length + (icon.Length & 1);
                int tables = (Sequence != null ? 8 + Sequence.Count * 4 : 0) + (Rates != null ? 8 + Rates.Count * 4 : 0);
                wr.Write(ascii.GetBytes("RIFF")); wr.Write(4 + 44 + tables + 8 + listLen); wr.Write(ascii.GetBytes("ACON"));
                wr.Write(ascii.GetBytes("anih")); wr.Write(36);
                wr.Write(36); wr.Write(Frames.Count); wr.Write(StepCount);
                wr.Write(0); wr.Write(0); wr.Write(32); wr.Write(1);
                wr.Write(Jiffies); wr.Write(table && Sequence != null ? 3 : 1); // AF_ICON | AF_SEQUENCE
                if (Sequence != null) { wr.Write(ascii.GetBytes("seq ")); wr.Write(Sequence.Count * 4); foreach (int v in Sequence) wr.Write(v); }
                if (Rates != null) { wr.Write(ascii.GetBytes("rate")); wr.Write(Rates.Count * 4); foreach (int v in Rates) wr.Write(v); }
                wr.Write(ascii.GetBytes("LIST")); wr.Write(listLen); wr.Write(ascii.GetBytes("fram"));
                foreach (byte[] icon in icons)
                {
                    wr.Write(ascii.GetBytes("icon")); wr.Write(icon.Length); wr.Write(icon);
                    if ((icon.Length & 1) != 0) wr.Write((byte)0);
                }
                wr.Flush();
                return ms.ToArray();
            }
        }

        public void Save(string path) { File.WriteAllBytes(path, ToBytes()); }
    }
}
