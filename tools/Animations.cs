using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

// Animation recipes for the "Lively" style. Each recipe is a list of poses applied
// to the role's own static artwork at every size. All looping effects share the
// 3.2 s / 48-step cycle of the v3 Arrow pulse, peak at the half-way step, so the
// family breathes together and frames k and 48-k are identical (stored once).
namespace CursorForge
{
    internal struct Pose
    {
        public double Glow;     // 0..1 of the Arrow pulse strength
        public double Degrees;  // rotation of the moving layer, clockwise
        public double Scale;
        public double Dx, Dy;   // translation as a fraction of the cursor size
        public double Light;    // 0..1 brightening + halo of the badge, geometry unchanged

        public static Pose Rest() { Pose p; p.Glow = 0; p.Degrees = 0; p.Scale = 1; p.Dx = 0; p.Dy = 0; p.Light = 0; return p; }
        public bool Moves { get { return Math.Abs(Degrees) > 1e-4 || Math.Abs(Scale - 1) > 1e-4 || Math.Abs(Dx) > 1e-5 || Math.Abs(Dy) > 1e-5; } }
        public string Key { get { return string.Format(CultureInfo.InvariantCulture, "{0:F4}|{1:F3}|{2:F4}|{3:F5}|{4:F5}|{5:F4}", Glow, Degrees, Scale, Dx, Dy, Light); } }
    }

    internal enum Pivot { Centroid, BottomCentre, CursorCentre }

    internal sealed class Recipe
    {
        public string Role;
        public string Base;      // role whose artwork stays still; the difference is the moving badge
        public Pivot Pivot;
        public string Effect;    // human-readable, written to the manifest
        public readonly List<Pose> Steps = new List<Pose>();
        public readonly List<int> Rates = new List<int>();
    }

    internal static class Animations
    {
        public static readonly int[] Sizes = { 32, 40, 48, 64, 128 };
        public const int CycleSteps = 48;
        public const int StepJiffies = 4;
        // Fitted against v3 Arrow.ani frame 24 (mean error under 0.25/255).
        public static double GlowSigma(int size) { return 0.05 * size + 0.15; }
        public const double GlowStrength = 0.15;

        static double Pulse(double t) { return 0.5 - 0.5 * Math.Cos(2 * Math.PI * t); }
        static double Bump(double t, double halfWidth)
        {
            double d = Math.Abs(t - 0.5) / halfWidth;
            return d >= 1 ? 0 : Math.Pow(Math.Cos(Math.PI / 2 * d), 2);
        }
        static double Hop(double t, double halfWidth)
        {
            double d = (t - 0.5) / halfWidth;
            return Math.Abs(d) >= 1 ? 0 : 1 - d * d; // ballistic arc, lands with a tap
        }

        static Recipe Looping(string role, string baseRole, Pivot pivot, string effect, Func<double, Pose> pose)
        {
            Recipe r = new Recipe { Role = role, Base = baseRole, Pivot = pivot, Effect = effect };
            for (int k = 0; k < CycleSteps; k++) { r.Steps.Add(pose(k / (double)CycleSteps)); r.Rates.Add(StepJiffies); }
            return r;
        }

        public static List<Recipe> All()
        {
            List<Recipe> list = new List<Recipe>();
            list.Add(Looping("Help", "Arrow", Pivot.Centroid, "3.2-second glow pulse in step with Arrow; the ? badge tilts curiously at the peak",
                delegate(double t) { Pose p = Pose.Rest(); p.Glow = Pulse(t); p.Degrees = -16 * Bump(t, 0.17); return p; }));
            list.Add(Looping("Hand", null, Pivot.Centroid, "3.2-second soft glow pulse in step with Arrow",
                delegate(double t) { Pose p = Pose.Rest(); p.Glow = Pulse(t); return p; }));
            list.Add(Looping("Pin", "Hand", Pivot.Centroid, "Glow pulse; the location pin hops and lands at the peak",
                delegate(double t) { Pose p = Pose.Rest(); p.Glow = Pulse(t); p.Dy = -0.06 * Hop(t, 0.11); return p; }));
            // The person badge is knocked out of the hand outline, so it lights up in
            // place (a presence ping) instead of moving and exposing the knockout.
            list.Add(Looping("Person", "Hand", Pivot.Centroid, "Glow pulse; the person badge lights up like a presence ping at the peak",
                delegate(double t) { Pose p = Pose.Rest(); p.Glow = Pulse(t); p.Light = Bump(t, 0.2); return p; }));

            // Unavailable: a quick damped head-shake of the slash at 30 fps, then a long rest.
            Recipe no = new Recipe { Role = "No", Pivot = Pivot.CursorCentre, Effect = "0.6-second damped shake, then rests for the remainder of a 3.2-second cycle" };
            const int shakeSteps = 17, shakeJiffies = 2;
            for (int k = 1; k <= shakeSteps; k++)
            {
                double tau = k / (double)(shakeSteps + 1);
                Pose p = Pose.Rest();
                p.Degrees = 14 * Math.Sin(2 * Math.PI * 2 * tau) * Math.Pow(1 - tau, 1.5);
                no.Steps.Add(p); no.Rates.Add(shakeJiffies);
            }
            no.Steps.Add(Pose.Rest()); no.Rates.Add(CycleSteps * StepJiffies - shakeSteps * shakeJiffies);
            list.Add(no);
            return list;
        }

        public static Recipe Find(string role)
        {
            foreach (Recipe r in All()) if (r.Role == role) return r;
            return null;
        }
    }

    internal sealed class Renderer
    {
        private readonly string root;
        private readonly Dictionary<string, CurFile> cache = new Dictionary<string, CurFile>();

        public Renderer(string root) { this.root = root; }

        public CurFile Static(string role)
        {
            CurFile c;
            if (!cache.TryGetValue(role, out c)) { c = CurFile.Read(Path.Combine(root, role + ".cur")); cache[role] = c; }
            return c;
        }

        public Img Render(Recipe r, int size, Pose p)
        {
            Img art = Static(r.Role).BySize(size).Pixels;
            if (p.Moves)
            {
                Img still = r.Base != null ? Static(r.Base).BySize(size).Pixels : null;
                Img moving = still != null ? Effects.Unover(art, still, 2) : art;
                double cx, cy, top, bottom, px, py;
                Effects.Bounds(moving, out cx, out cy, out top, out bottom);
                switch (r.Pivot)
                {
                    case Pivot.BottomCentre: px = cx; py = bottom; break;
                    case Pivot.CursorCentre: px = size / 2.0; py = size / 2.0; break;
                    default: px = cx; py = cy; break;
                }
                Img moved = Effects.Transform(moving, p.Scale, p.Degrees, px, py, p.Dx * size, p.Dy * size);
                art = still != null ? Effects.Over(moved, still) : moved;
            }
            if (p.Light > 0 && r.Base != null)
            {
                Img badge = Effects.Unover(art, Static(r.Base).BySize(size).Pixels, 2);
                art = Effects.Lighten(art, badge, 0.45 * p.Light);
                float[] halo = Effects.Blur(Effects.AlphaOf(badge, 128), size, size, 0.07 * size + 0.2);
                art = Effects.Under(art, Effects.FadeEdges(halo, size, size, 0.08 * size), 0.55 * p.Light, 120, 180, 255);
            }
            if (p.Glow > 0) art = Effects.Glow(art, Animations.GlowSigma(size), Animations.GlowStrength * p.Glow);
            return art;
        }

        public CurFile Frame(Recipe r, Pose p)
        {
            CurFile src = Static(r.Role);
            CurFile f = new CurFile();
            foreach (int size in Animations.Sizes)
            {
                CurImage s = src.BySize(size);
                f.Images.Add(new CurImage { HotX = s.HotX, HotY = s.HotY, Pixels = Render(r, size, p) });
            }
            return f;
        }

        public AniFile Build(Recipe r)
        {
            List<CurFile> frames = new List<CurFile>();
            List<int> sequence = new List<int>();
            Dictionary<string, int> byPose = new Dictionary<string, int>();
            foreach (Pose p in r.Steps)
            {
                int index;
                if (!byPose.TryGetValue(p.Key, out index))
                {
                    index = frames.Count;
                    frames.Add(Frame(r, p));
                    byPose[p.Key] = index;
                }
                sequence.Add(index);
            }
            return Packer.Pack(frames, sequence, new List<int>(r.Rates), Animations.StepJiffies);
        }
    }

    internal static class Packer
    {
        // Removes byte-identical frames, merges consecutive repeats into one longer step,
        // and drops the step tables entirely when they would be the identity.
        public static AniFile Pack(List<CurFile> frames, List<int> sequence, List<int> rates, int defaultJiffies)
        {
            List<byte[]> blobs = new List<byte[]>();
            List<int> remap = new List<int>();
            AniFile ani = new AniFile();
            for (int i = 0; i < frames.Count; i++)
            {
                byte[] b = frames[i].ToBytes();
                int found = -1;
                for (int j = 0; j < blobs.Count && found < 0; j++) if (Same(blobs[j], b)) found = j;
                if (found < 0) { found = blobs.Count; blobs.Add(b); ani.Frames.Add(frames[i]); }
                remap.Add(found);
            }
            List<int> seq = new List<int>(), rate = new List<int>();
            for (int s = 0; s < sequence.Count; s++)
            {
                int f = remap[sequence[s]];
                if (seq.Count > 0 && seq[seq.Count - 1] == f) rate[rate.Count - 1] += rates[s];
                else { seq.Add(f); rate.Add(rates[s]); }
            }
            bool identity = seq.Count == ani.Frames.Count;
            for (int s = 0; identity && s < seq.Count; s++) identity = seq[s] == s;
            bool uniform = true;
            foreach (int v in rate) if (v != rate[0]) uniform = false;
            ani.Jiffies = uniform ? rate[0] : defaultJiffies;
            ani.Sequence = identity ? null : seq;
            ani.Rates = uniform ? null : rate;
            if (ani.Sequence == null && ani.Rates != null && ani.Rates.Count != ani.Frames.Count) throw new InvalidDataException("Rate table mismatch.");
            return ani;
        }

        public static AniFile Repack(AniFile source)
        {
            List<int> seq = new List<int>(), rates = new List<int>();
            for (int s = 0; s < source.StepCount; s++) { seq.Add(source.FrameAt(s)); rates.Add(source.RateAt(s)); }
            return Pack(source.Frames, seq, rates, source.Jiffies);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
