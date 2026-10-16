using System;
using System.Drawing;
using System.Runtime.InteropServices;

// Read-only probes of how Windows itself loads and renders a cursor file. Nothing here
// changes a system cursor; handles are created privately and destroyed after use.
namespace CursorForge
{
    internal static class WindowsCursor
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr cursor);
        [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int cx, int cy, int step, IntPtr brush, int flags);
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        // Undocumented but stable since Windows 2000; also implemented by Wine.
        [DllImport("user32.dll")] private static extern IntPtr GetCursorFrameInfo(IntPtr cursor, int reserved, int step, out int rateJiffies, out int steps);
        [DllImport("user32.dll")] private static extern uint SetThreadCursorCreationScaling(uint dpi);

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO { public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }

        public sealed class Probe
        {
            public int Steps;
            public int[] RatesJiffies;
            public int HotX, HotY;
        }

        public static IntPtr Load(string path, int size)
        {
            uint old = 0;
            try { old = SetThreadCursorCreationScaling(96); } catch (EntryPointNotFoundException) { }
            try
            {
                IntPtr h = LoadImage(IntPtr.Zero, path, 2, size, size, 0x10);
                if (h == IntPtr.Zero) throw new InvalidOperationException("Windows could not load " + path + " at " + size + "px (error " + Marshal.GetLastWin32Error() + ").");
                return h;
            }
            finally { if (old != 0) try { SetThreadCursorCreationScaling(old); } catch (EntryPointNotFoundException) { } }
        }

        public static Probe Inspect(string path, int size)
        {
            IntPtr h = Load(path, size);
            try
            {
                Probe p = new Probe();
                int rate, steps;
                GetCursorFrameInfo(h, 0, 0, out rate, out steps);
                p.Steps = Math.Max(1, steps);
                p.RatesJiffies = new int[p.Steps];
                for (int i = 0; i < p.Steps; i++) { int s; GetCursorFrameInfo(h, 0, i, out rate, out s); p.RatesJiffies[i] = rate; }
                ICONINFO info;
                if (GetIconInfo(h, out info))
                {
                    p.HotX = info.xHotspot; p.HotY = info.yHotspot;
                    if (info.hbmMask != IntPtr.Zero) DeleteObject(info.hbmMask);
                    if (info.hbmColor != IntPtr.Zero) DeleteObject(info.hbmColor);
                }
                return p;
            }
            finally { DestroyCursor(h); }
        }

        // Renders one animation step the way Windows draws it, over an opaque background.
        public static Img Render(string path, int size, int step, Color background)
        {
            return RenderSteps(path, size, new[] { step }, background)[0];
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight; public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        // Draws with DrawIconEx into a 32-bit top-down DIB section, so Windows alpha-blends
        // the cursor exactly as on screen. (A GDI+ HDC has no per-pixel alpha and makes
        // Windows fall back to a mask derived from alpha > 0, which looks like a dark halo.)
        public static Img[] RenderSteps(string path, int size, int[] steps, Color background)
        {
            IntPtr h = Load(path, size);
            try
            {
                Img[] result = new Img[steps.Length];
                BITMAPINFOHEADER bmi = new BITMAPINFOHEADER { biSize = 40, biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
                IntPtr bits;
                IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
                IntPtr dib = CreateDIBSection(dc, ref bmi, 0, out bits, IntPtr.Zero, 0);
                IntPtr old = SelectObject(dc, dib);
                try
                {
                    byte[] buffer = new byte[size * size * 4];
                    for (int i = 0; i < steps.Length; i++)
                    {
                        for (int p = 0; p < buffer.Length; p += 4)
                        { buffer[p] = background.B; buffer[p + 1] = background.G; buffer[p + 2] = background.R; buffer[p + 3] = 255; }
                        Marshal.Copy(buffer, 0, bits, buffer.Length);
                        DrawIconEx(dc, 0, 0, h, size, size, steps[i], IntPtr.Zero, 3);
                        GdiFlush();
                        Img img = new Img(size, size);
                        Marshal.Copy(bits, img.P, 0, img.P.Length);
                        for (int p = 3; p < img.P.Length; p += 4) img.P[p] = 255;
                        result[i] = img;
                    }
                }
                finally { SelectObject(dc, old); DeleteObject(dib); DeleteDC(dc); }
                return result;
            }
            finally { DestroyCursor(h); }
        }

        public static Img Composite(Img art, Color background)
        {
            Img o = new Img(art.W, art.H);
            for (int i = 0; i < art.W * art.H; i++)
            {
                int p = i * 4;
                double a = art.P[p + 3] / 255.0;
                o.P[p] = Effects.Clamp(art.P[p] * a + background.B * (1 - a));
                o.P[p + 1] = Effects.Clamp(art.P[p + 1] * a + background.G * (1 - a));
                o.P[p + 2] = Effects.Clamp(art.P[p + 2] * a + background.R * (1 - a));
                o.P[p + 3] = 255;
            }
            return o;
        }
    }
}
