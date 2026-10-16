using System;

// Pixel operations used to derive the animated roles from the hand-tuned static
// artwork. Every size is processed from its own static image, so small sizes keep
// their pixel hinting instead of being scaled down from 128 px.
namespace CursorForge
{
    internal static class Effects
    {
        // Soft-glow colour measured from the v3 Arrow.ani pulse.
        public const byte GlowR = 77, GlowG = 125, GlowB = 214;

        public static float[] AlphaOf(Img img, int minAlpha)
        {
            float[] m = new float[img.W * img.H];
            for (int i = 0; i < m.Length; i++)
            {
                int a = img.P[i * 4 + 3];
                m[i] = a >= minAlpha ? a / 255f : 0f;
            }
            return m;
        }

        public static float[] Blur(float[] src, int w, int h, double sigma)
        {
            int radius = (int)Math.Ceiling(sigma * 3);
            float[] k = new float[radius * 2 + 1];
            double total = 0;
            for (int i = -radius; i <= radius; i++) { k[i + radius] = (float)Math.Exp(-(i * i) / (2 * sigma * sigma)); total += k[i + radius]; }
            for (int i = 0; i < k.Length; i++) k[i] = (float)(k[i] / total);
            float[] tmp = new float[w * h];
            float[] dst = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0;
                    for (int i = -radius; i <= radius; i++) { int xx = x + i; if (xx >= 0 && xx < w) s += src[y * w + xx] * k[i + radius]; }
                    tmp[y * w + x] = s;
                }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float s = 0;
                    for (int i = -radius; i <= radius; i++) { int yy = y + i; if (yy >= 0 && yy < h) s += tmp[yy * w + x] * k[i + radius]; }
                    dst[y * w + x] = s;
                }
            return dst;
        }

        // Places a tinted glow layer underneath the artwork.
        public static Img Under(Img top, float[] glow, double amp, byte r, byte g, byte b)
        {
            Img bottom = new Img(top.W, top.H);
            for (int i = 0; i < glow.Length; i++)
            {
                double a = Math.Min(1.0, glow[i] * amp);
                bottom.P[i * 4] = b; bottom.P[i * 4 + 1] = g; bottom.P[i * 4 + 2] = r;
                bottom.P[i * 4 + 3] = (byte)Math.Round(a * 255);
            }
            return Over(top, bottom);
        }

        public static Img Glow(Img art, double sigma, double amp)
        {
            if (amp <= 0) return art.Clone();
            return Under(art, Blur(AlphaOf(art, 0), art.W, art.H, sigma), amp, GlowR, GlowG, GlowB);
        }

        // Straight-alpha source-over.
        public static Img Over(Img top, Img bottom)
        {
            Img o = new Img(top.W, top.H);
            for (int i = 0; i < top.W * top.H; i++)
            {
                int p = i * 4;
                double ta = top.P[p + 3] / 255.0, ba = bottom.P[p + 3] / 255.0;
                double oa = ta + ba * (1 - ta);
                if (oa <= 0) continue;
                for (int c = 0; c < 3; c++)
                    o.P[p + c] = Clamp((top.P[p + c] * ta + bottom.P[p + c] * ba * (1 - ta)) / oa);
                o.P[p + 3] = Clamp(oa * 255);
            }
            return o;
        }

        // Recovers the layer that was composited over `bottom` to give `composite`,
        // restricted to pixels that actually differ. Used to lift badges off a base role.
        public static Img Unover(Img composite, Img bottom, int tolerance)
        {
            Img top = new Img(composite.W, composite.H);
            for (int i = 0; i < composite.W * composite.H; i++)
            {
                int p = i * 4;
                bool differs = false;
                for (int c = 0; c < 4; c++) if (Math.Abs(composite.P[p + c] - bottom.P[p + c]) > tolerance) differs = true;
                if (!differs) continue;
                double ca = composite.P[p + 3] / 255.0, ba = bottom.P[p + 3] / 255.0;
                double ta = ba >= 0.999 ? 1.0 : Math.Max(0, (ca - ba) / (1 - ba));
                if (ta <= 0.002) continue;
                for (int c = 0; c < 3; c++)
                {
                    double v = (composite.P[p + c] * ca - bottom.P[p + c] * ba * (1 - ta)) / ta;
                    top.P[p + c] = Clamp(v);
                }
                top.P[p + 3] = Clamp(ta * 255);
            }
            return top;
        }

        // Removes `layer` from `composite` where it is the dominant contributor, leaving the base.
        public static Img Erase(Img composite, Img layer)
        {
            Img o = composite.Clone();
            for (int i = 0; i < o.W * o.H; i++) if (layer.P[i * 4 + 3] > 0) { o.P[i * 4] = o.P[i * 4 + 1] = o.P[i * 4 + 2] = o.P[i * 4 + 3] = 0; }
            return o;
        }

        // Affine resample (scale + rotation about a pivot, then translation) with
        // bilinear filtering in premultiplied space.
        public static Img Transform(Img src, double scale, double degrees, double pivotX, double pivotY, double dx, double dy)
        {
            Img o = new Img(src.W, src.H);
            double rad = degrees * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
            for (int y = 0; y < src.H; y++)
                for (int x = 0; x < src.W; x++)
                {
                    // inverse mapping of the pixel centre
                    double px = x + 0.5 - pivotX - dx, py = y + 0.5 - pivotY - dy;
                    double sx = (cos * px + sin * py) / scale + pivotX - 0.5;
                    double sy = (-sin * px + cos * py) / scale + pivotY - 0.5;
                    int x0 = (int)Math.Floor(sx), y0 = (int)Math.Floor(sy);
                    double fx = sx - x0, fy = sy - y0;
                    double a = 0, r = 0, g = 0, b = 0;
                    for (int j = 0; j < 2; j++)
                        for (int i = 0; i < 2; i++)
                        {
                            int xx = x0 + i, yy = y0 + j;
                            if (xx < 0 || yy < 0 || xx >= src.W || yy >= src.H) continue;
                            double wgt = (i == 0 ? 1 - fx : fx) * (j == 0 ? 1 - fy : fy);
                            int p = (yy * src.W + xx) * 4;
                            double pa = src.P[p + 3] / 255.0 * wgt;
                            a += pa; b += src.P[p] * pa; g += src.P[p + 1] * pa; r += src.P[p + 2] * pa;
                        }
                    if (a <= 0.0005) continue;
                    int q = (y * src.W + x) * 4;
                    o.P[q] = Clamp(b / a); o.P[q + 1] = Clamp(g / a); o.P[q + 2] = Clamp(r / a);
                    o.P[q + 3] = Clamp(a * 255);
                }
            return o;
        }

        // Fades a mask to zero toward the canvas border so a halo never ends in a hard line.
        public static float[] FadeEdges(float[] mask, int w, int h, double margin)
        {
            float[] o = new float[mask.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double d = Math.Min(Math.Min(x + 0.5, w - x - 0.5), Math.Min(y + 0.5, h - y - 0.5));
                    o[y * w + x] = (float)(mask[y * w + x] * Ease(d / margin));
                }
            return o;
        }

        // Brightens the saturated blue fill of a badge toward a pale highlight.
        public static Img Lighten(Img art, Img badge, double amount)
        {
            Img o = art.Clone();
            for (int i = 0; i < o.W * o.H; i++)
            {
                int p = i * 4;
                if (badge.P[p + 3] == 0) continue;
                int b = o.P[p], g = o.P[p + 1], r = o.P[p + 2];
                double blue = Math.Max(0, Math.Min(1, (b - r - 40) / 80.0)); // only the blue fill, not outline/glow
                double k = amount * blue;
                o.P[p] = Clamp(b + (255 - b) * k);
                o.P[p + 1] = Clamp(g + (222 - g) * k);
                o.P[p + 2] = Clamp(r + (178 - r) * k);
            }
            return o;
        }

        public static void Bounds(Img img, out double cx, out double cy, out double top, out double bottom)
        {
            double sx = 0, sy = 0, sw = 0; int minY = img.H, maxY = -1;
            for (int y = 0; y < img.H; y++)
                for (int x = 0; x < img.W; x++)
                {
                    int a = img.P[(y * img.W + x) * 4 + 3];
                    if (a == 0) continue;
                    sx += (x + 0.5) * a; sy += (y + 0.5) * a; sw += a;
                    if (a > 128) { if (y < minY) minY = y; if (y > maxY) maxY = y; }
                }
            cx = sw > 0 ? sx / sw : img.W / 2.0;
            cy = sw > 0 ? sy / sw : img.H / 2.0;
            top = minY; bottom = maxY + 1;
        }

        public static byte Clamp(double v) { return (byte)(v <= 0 ? 0 : v >= 255 ? 255 : Math.Round(v)); }

        public static double Ease(double t) { return t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t); }
    }
}
