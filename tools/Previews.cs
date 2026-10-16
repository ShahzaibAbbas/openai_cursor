using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;

namespace CursorForge
{
    internal static class Previews
    {
        static readonly string[][] GifCells =
        {
            new[] { "Arrow", "Normal \u00b7 glow pulse" },
            new[] { "Help", "Help \u00b7 curious tilt" },
            new[] { "Hand", "Link \u00b7 soft pulse" },
            new[] { "Pin", "Location \u00b7 hop" },
            new[] { "Person", "Person \u00b7 presence" },
            new[] { "No", "Unavailable \u00b7 shake" },
        };

        // Samples every animation on a shared 30 fps clock for one 3.2 s cycle.
        public static void Gif(string root, string outPath)
        {
            const int cell = 150, size = 128, width = cell * 6, height = 206, tick = 2; // jiffies per sample
            Color bg = Color.FromArgb(27, 35, 48);
            List<AniFile> anis = new List<AniFile>();
            foreach (string[] c in GifCells) anis.Add(AniFile.Read(Path.Combine(root, c[0] + ".ani")));
            int total = Animations.CycleSteps * Animations.StepJiffies;
            List<Img> frames = new List<Img>();
            using (Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(canvas))
            using (Font font = new Font("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush text = new SolidBrush(Color.FromArgb(206, 214, 226)))
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int t = 0; t < total; t += tick)
                {
                    g.Clear(bg);
                    for (int i = 0; i < anis.Count; i++)
                    {
                        using (Bitmap art = StepAt(anis[i], t).BySize(size).Pixels.ToBitmap())
                            g.DrawImageUnscaled(art, i * cell + (cell - size) / 2, 14);
                        g.DrawString(GifCells[i][1], font, text, new RectangleF(i * cell, 160, cell, 30), centre);
                    }
                    frames.Add(Img.FromBitmap(canvas));
                }
            }
            GifWriter.Write(outPath, frames, tick * 100.0 / 60.0);
            Console.WriteLine("{0}: {1} samples, {2:N0} bytes", outPath, frames.Count, new FileInfo(outPath).Length);
        }

        // Overview of all roles in the layout of the v3 scheme-preview.png.
        public static void Scheme(string root, string outPath)
        {
            Dictionary<string, object> roles = (Dictionary<string, object>)((Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer()
                .DeserializeObject(File.ReadAllText(Path.Combine(root, "cursor-manifest.json"))))["roles"];
            const int cols = 4, cardW = 259, cardH = 170, gap = 15, left = 24, top = 110;
            int rows = (Program.Roles.Length + cols - 1) / cols;
            int width = left * 2 + cols * cardW + (cols - 1) * gap, height = top + rows * cardH + (rows - 1) * 14 + 34;
            Color page = Color.FromArgb(243, 244, 248), ink = Color.FromArgb(22, 27, 36), sub = Color.FromArgb(98, 108, 122);
            Color wellLight = Color.FromArgb(245, 247, 251), wellDark = Color.FromArgb(30, 38, 51);
            using (Bitmap b = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(b))
            using (Font title = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Point))
            using (Font subtitle = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Point))
            using (Font label = new Font("Segoe UI Semibold", 10.5f, FontStyle.Regular, GraphicsUnit.Point))
            using (Font small = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point))
            using (SolidBrush inkBrush = new SolidBrush(ink))
            using (SolidBrush subBrush = new SolidBrush(sub))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush lightBrush = new SolidBrush(wellLight))
            using (SolidBrush darkBrush = new SolidBrush(wellDark))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(page);
                g.DrawString("SOFT BLACK  /  V4", title, inkBrush, 28, 24);
                g.DrawString("Smooth contours  ·  Transparent glow  ·  17 Windows cursor roles  ·  Lively, Classic and Static styles", subtitle, subBrush, 30, 64);
                for (int i = 0; i < Program.Roles.Length; i++)
                {
                    string role = Program.Roles[i];
                    int x = left + (i % cols) * (cardW + gap), y = top + (i / cols) * (cardH + 14);
                    using (GraphicsPath card = Rounded(new Rectangle(x, y, cardW, cardH), 10)) g.FillPath(white, card);
                    g.DrawString((string)((Dictionary<string, object>)roles[role])["label"], label, inkBrush, x + 12, y + 13);
                    CurFile cur = CurFile.Read(Path.Combine(root, role + ".cur"));
                    Rectangle l = new Rectangle(x + 10, y + 43, 117, 90), d = new Rectangle(x + 132, y + 43, 117, 90);
                    using (GraphicsPath p = Rounded(l, 6)) g.FillPath(lightBrush, p);
                    using (GraphicsPath p = Rounded(d, 6)) g.FillPath(darkBrush, p);
                    using (Bitmap art = cur.BySize(64).Pixels.ToBitmap())
                    {
                        g.DrawImageUnscaled(art, l.X + (l.Width - 64) / 2, l.Y + (l.Height - 64) / 2);
                        g.DrawImageUnscaled(art, d.X + (d.Width - 64) / 2, d.Y + (d.Height - 64) / 2);
                    }
                    bool lively = Program.IsAnimated(role, "Lively"), classic = Program.IsAnimated(role, "Classic");
                    string tag = classic ? "animated  /  32–128 px" : lively ? "animated in Lively  /  32–128 px" : "static  /  32–128 px";
                    g.DrawString(tag, small, subBrush, x + 12, y + 144);
                    using (Bitmap tiny = cur.BySize(32).Pixels.ToBitmap()) g.DrawImageUnscaled(tiny, x + cardW - 44, y + cardH - 37);
                }
                b.Save(outPath, ImageFormat.Png);
            }
            Console.WriteLine("{0}: {1}x{2}", outPath, width, height);
        }

        static GraphicsPath Rounded(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        internal static CurFile StepAt(AniFile ani, int jiffy)
        {
            int t = jiffy % ani.TotalJiffies;
            for (int s = 0; s < ani.StepCount; s++)
            {
                if (t < ani.RateAt(s)) return ani.Frames[ani.FrameAt(s)];
                t -= ani.RateAt(s);
            }
            return ani.Frames[ani.FrameAt(0)];
        }

        // Application/tray icon: the Arrow artwork centred in each icon size.
        public static void Icon(string root, string outPath)
        {
            Img art = CurFile.Read(Path.Combine(root, "Arrow.cur")).BySize(128).Pixels;
            int minX = art.W, minY = art.H, maxX = 0, maxY = 0;
            for (int y = 0; y < art.H; y++) for (int x = 0; x < art.W; x++)
                if (art.A(x, y) > 8) { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            int[] sizes = { 16, 20, 24, 32, 48, 64, 128 };
            List<byte[]> dibs = new List<byte[]>();
            using (Bitmap src = art.ToBitmap())
            {
                foreach (int s in sizes)
                {
                    using (Bitmap b = new Bitmap(s, s, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(b))
                        {
                            g.Clear(Color.Transparent);
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.CompositingQuality = CompositingQuality.HighQuality;
                            float w = maxX - minX + 1, h = maxY - minY + 1;
                            float scale = (s * 0.94f) / Math.Max(w, h);
                            float dw = w * scale, dh = h * scale;
                            using (ImageAttributes ia = new ImageAttributes())
                            {
                                ia.SetWrapMode(WrapMode.TileFlipXY);
                                g.DrawImage(src, new Rectangle((int)Math.Round((s - dw) / 2), (int)Math.Round((s - dh) / 2), (int)Math.Round(dw), (int)Math.Round(dh)),
                                    minX, minY, w, h, GraphicsUnit.Pixel, ia);
                            }
                        }
                        if (s >= 64)
                            using (MemoryStream png = new MemoryStream()) { b.Save(png, ImageFormat.Png); dibs.Add(png.ToArray()); } // PNG entries (Vista+) keep the exe small
                        else dibs.Add(CurFile.WriteDib(Img.FromBitmap(b)));
                    }
                }
            }
            using (BinaryWriter wr = new BinaryWriter(File.Create(outPath)))
            {
                wr.Write((ushort)0); wr.Write((ushort)1); wr.Write((ushort)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    wr.Write((byte)sizes[i]); wr.Write((byte)sizes[i]); wr.Write((byte)0); wr.Write((byte)0);
                    wr.Write((ushort)1); wr.Write((ushort)32); wr.Write(dibs[i].Length); wr.Write(offset);
                    offset += dibs[i].Length;
                }
                foreach (byte[] d in dibs) wr.Write(d);
            }
            Console.WriteLine("{0}: {1} sizes", outPath, sizes.Length);
        }
    }

    // GIF89a writer: one median-cut global palette, changed-rectangle frames, looping.
    internal static class GifWriter
    {
        public static void Write(string path, List<Img> frames, double centisecondsPerFrame)
        {
            int w = frames[0].W, h = frames[0].H;
            byte[][] palette = BuildPalette(frames);
            byte[] lookup = new byte[32768];
            for (int c = 0; c < 32768; c++)
            {
                int r = ((c >> 10) & 31) * 255 / 31, g = ((c >> 5) & 31) * 255 / 31, b = (c & 31) * 255 / 31;
                int best = 0, bestD = int.MaxValue;
                for (int i = 0; i < 256; i++)
                {
                    int dr = r - palette[i][0], dg = g - palette[i][1], db = b - palette[i][2];
                    int d = 2 * dr * dr + 4 * dg * dg + 3 * db * db;
                    if (d < bestD) { bestD = d; best = i; }
                }
                lookup[c] = (byte)best;
            }
            List<byte[]> indexed = new List<byte[]>();
            foreach (Img f in frames)
            {
                byte[] ix = new byte[w * h];
                for (int i = 0; i < ix.Length; i++)
                    ix[i] = lookup[((f.P[i * 4 + 2] >> 3) << 10) | ((f.P[i * 4 + 1] >> 3) << 5) | (f.P[i * 4] >> 3)];
                indexed.Add(ix);
            }
            using (BinaryWriter wr = new BinaryWriter(File.Create(path)))
            {
                wr.Write(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
                wr.Write((ushort)w); wr.Write((ushort)h);
                wr.Write((byte)0xF7); wr.Write((byte)0); wr.Write((byte)0);
                foreach (byte[] p in palette) wr.Write(p, 0, 3);
                wr.Write(new byte[] { 0x21, 0xFF, 0x0B });
                wr.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                wr.Write(new byte[] { 3, 1, 0, 0, 0 });
                double clock = 0; int written = 0;
                byte[] previous = null;
                int pendingIndex = -1; int px = 0, py = 0, pw = 0, ph = 0;
                for (int f = 0; f <= indexed.Count; f++)
                {
                    int x0 = 0, y0 = 0, x1 = w - 1, y1 = h - 1;
                    bool changed = true;
                    if (f < indexed.Count && previous != null)
                    {
                        x0 = w; y0 = h; x1 = -1; y1 = -1;
                        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                            if (indexed[f][y * w + x] != previous[y * w + x]) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                        changed = x1 >= 0;
                    }
                    if (f < indexed.Count && !changed) continue;
                    if (pendingIndex >= 0)
                    {
                        // Delay of the pending frame runs until this frame starts.
                        double end = f * centisecondsPerFrame;
                        int delay = (int)Math.Round(end - clock);
                        clock += delay;
                        WriteFrame(wr, indexed[pendingIndex], w, px, py, pw, ph, Math.Max(2, delay));
                        written++;
                    }
                    if (f == indexed.Count) break;
                    pendingIndex = f; px = x0; py = y0; pw = x1 - x0 + 1; ph = y1 - y0 + 1;
                    previous = indexed[f];
                }
                wr.Write((byte)0x3B);
                Console.WriteLine("GIF frames written: {0}", written);
            }
        }

        static void WriteFrame(BinaryWriter wr, byte[] ix, int stride, int x, int y, int w, int h, int delay)
        {
            wr.Write(new byte[] { 0x21, 0xF9, 4, 0x04 }); // disposal: leave in place
            wr.Write((ushort)delay); wr.Write((byte)0); wr.Write((byte)0);
            wr.Write((byte)0x2C); wr.Write((ushort)x); wr.Write((ushort)y); wr.Write((ushort)w); wr.Write((ushort)h); wr.Write((byte)0);
            byte[] sub = new byte[w * h];
            for (int r = 0; r < h; r++) Buffer.BlockCopy(ix, (y + r) * stride + x, sub, r * w, w);
            wr.Write((byte)8);
            byte[] data = Lzw(sub);
            for (int p = 0; p < data.Length; p += 255)
            {
                int n = Math.Min(255, data.Length - p);
                wr.Write((byte)n); wr.Write(data, p, n);
            }
            wr.Write((byte)0);
        }

        static byte[] Lzw(byte[] pixels)
        {
            const int clear = 256, eoi = 257;
            MemoryStream ms = new MemoryStream();
            int bitBuf = 0, bitCount = 0, codeSize = 9, next = 258;
            Dictionary<int, int> dict = new Dictionary<int, int>();
            Action<int> emit = delegate(int code)
            {
                bitBuf |= code << bitCount; bitCount += codeSize;
                while (bitCount >= 8) { ms.WriteByte((byte)(bitBuf & 0xFF)); bitBuf >>= 8; bitCount -= 8; }
            };
            emit(clear);
            int prefix = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                int k = pixels[i];
                int key = (prefix << 8) | k, code;
                if (dict.TryGetValue(key, out code)) { prefix = code; continue; }
                emit(prefix);
                if (next < 4096)
                {
                    dict[key] = next++;
                    if (next > (1 << codeSize) && codeSize < 12) codeSize++;
                }
                else { emit(clear); dict.Clear(); next = 258; codeSize = 9; }
                prefix = k;
            }
            emit(prefix);
            emit(eoi);
            if (bitCount > 0) ms.WriteByte((byte)(bitBuf & 0xFF));
            return ms.ToArray();
        }

        sealed class Box { public List<int> Colors; }

        static byte[][] BuildPalette(List<Img> frames)
        {
            int[] count = new int[32768];
            foreach (Img f in frames)
                for (int i = 0; i < f.W * f.H; i++)
                    count[((f.P[i * 4 + 2] >> 3) << 10) | ((f.P[i * 4 + 1] >> 3) << 5) | (f.P[i * 4] >> 3)]++;
            List<int> all = new List<int>();
            for (int c = 0; c < 32768; c++) if (count[c] > 0) all.Add(c);
            List<Box> boxes = new List<Box> { new Box { Colors = all } };
            while (boxes.Count < 256)
            {
                Box pick = null; long bestScore = 0; int axis = 0;
                foreach (Box b in boxes)
                {
                    if (b.Colors.Count < 2) continue;
                    int[] lo = { 31, 31, 31 }, hi = { 0, 0, 0 }; long pop = 0;
                    foreach (int c in b.Colors)
                    {
                        int[] ch = { (c >> 10) & 31, (c >> 5) & 31, c & 31 };
                        for (int a = 0; a < 3; a++) { lo[a] = Math.Min(lo[a], ch[a]); hi[a] = Math.Max(hi[a], ch[a]); }
                        pop += count[c];
                    }
                    for (int a = 0; a < 3; a++)
                    {
                        long score = (long)(hi[a] - lo[a]) * (long)Math.Sqrt(pop) * (a == 1 ? 4 : a == 0 ? 2 : 3);
                        if (score > bestScore) { bestScore = score; pick = b; axis = a; }
                    }
                }
                if (pick == null) break;
                int shift = axis == 0 ? 10 : axis == 1 ? 5 : 0;
                pick.Colors.Sort(delegate(int p, int q) { return ((p >> shift) & 31).CompareTo((q >> shift) & 31); });
                long half = 0, totalPop = 0;
                foreach (int c in pick.Colors) totalPop += count[c];
                int split = 0;
                for (; split < pick.Colors.Count - 1; split++) { half += count[pick.Colors[split]]; if (half * 2 >= totalPop) break; }
                split = Math.Max(1, Math.Min(pick.Colors.Count - 1, split + 1));
                Box other = new Box { Colors = pick.Colors.GetRange(split, pick.Colors.Count - split) };
                pick.Colors = pick.Colors.GetRange(0, split);
                boxes.Add(other);
            }
            byte[][] palette = new byte[256][];
            for (int i = 0; i < 256; i++)
            {
                if (i >= boxes.Count) { palette[i] = new byte[3]; continue; }
                double r = 0, g = 0, b = 0, n = 0;
                foreach (int c in boxes[i].Colors)
                {
                    r += ((c >> 10) & 31) * count[c]; g += ((c >> 5) & 31) * count[c]; b += (c & 31) * count[c]; n += count[c];
                }
                palette[i] = new[] { (byte)Math.Round(r / n * 255 / 31), (byte)Math.Round(g / n * 255 / 31), (byte)Math.Round(b / n * 255 / 31) };
            }
            return palette;
        }
    }
}
