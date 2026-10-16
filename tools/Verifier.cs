using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

// Loads every package cursor through Windows (LoadImage + DrawIconEx, never
// SetSystemCursor) and checks hotspots, step tables and rendered pixels.
namespace CursorForge
{
    internal static class Verifier
    {
        public static int Run(string root, string outPath)
        {
            JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> manifest = (Dictionary<string, object>)json.DeserializeObject(File.ReadAllText(Path.Combine(root, "cursor-manifest.json")));
            Dictionary<string, object> roles = (Dictionary<string, object>)manifest["roles"];
            List<object> records = new List<object>();
            List<string> failures = new List<string>();
            int renders = 0;

            foreach (string role in Program.Roles)
            {
                Dictionary<string, object> hotspots = (Dictionary<string, object>)((Dictionary<string, object>)roles[role])["hotspots"];
                foreach (string ext in new[] { ".cur", ".ani" })
                {
                    string path = Path.Combine(root, role + ext);
                    if (!File.Exists(path)) continue;
                    bool animated = ext == ".ani";
                    AniFile ani = animated ? AniFile.Read(path) : null;
                    CurFile cur = animated ? null : CurFile.Read(path);
                    int[] sizes = animated ? Animations.Sizes : new[] { 32, 40, 48, 64, 96, 128 };
                    double worstMean = 0; int worstMax = 0; bool hotspotsOk = true, stepsOk = true;
                    foreach (int size in sizes)
                    {
                        object[] want = (object[])hotspots[size.ToString()];
                        WindowsCursor.Probe probe = WindowsCursor.Inspect(path, size);
                        if (probe.HotX != Convert.ToInt32(want[0]) || probe.HotY != Convert.ToInt32(want[1]))
                        {
                            hotspotsOk = false;
                            failures.Add(string.Format("{0}{1} @{2}: hotspot {3},{4} expected {5},{6}", role, ext, size, probe.HotX, probe.HotY, want[0], want[1]));
                        }
                        int stepCount = animated ? ani.StepCount : 1;
                        if (animated)
                        {
                            if (probe.Steps != stepCount) { stepsOk = false; failures.Add(role + ext + ": Windows reports " + probe.Steps + " steps, file has " + stepCount); }
                            for (int s = 0; s < Math.Min(stepCount, probe.Steps); s++)
                                if (probe.RatesJiffies[s] != ani.RateAt(s)) { stepsOk = false; failures.Add(role + ext + ": step " + s + " rate " + probe.RatesJiffies[s] + " expected " + ani.RateAt(s)); }
                        }
                        int[] steps = new int[stepCount];
                        for (int s = 0; s < stepCount; s++) steps[s] = s;
                        foreach (Color bg in new[] { Program.Light, Program.Dark })
                        {
                            Img[] got = WindowsCursor.RenderSteps(path, size, steps, bg);
                            for (int s = 0; s < stepCount; s++)
                            {
                                Img art = animated ? ani.Frames[ani.FrameAt(s)].BySize(size).Pixels : cur.BySize(size).Pixels;
                                Img expected = WindowsCursor.Composite(art, bg);
                                long sum = 0; int max = 0;
                                for (int i = 0; i < expected.P.Length; i += 4)
                                    for (int c = 0; c < 3; c++)
                                    {
                                        int d = Math.Abs(expected.P[i + c] - got[s].P[i + c]);
                                        sum += d; if (d > max) max = d;
                                    }
                                double mean = sum / (double)(size * size * 3);
                                worstMean = Math.Max(worstMean, mean); worstMax = Math.Max(worstMax, max);
                                renders++;
                            }
                        }
                    }
                    if (worstMean > 1.0 || worstMax > 12) failures.Add(string.Format("{0}{1}: render error mean {2:F3} max {3}", role, ext, worstMean, worstMax));
                    Dictionary<string, object> rec = new Dictionary<string, object>();
                    rec["file"] = role + ext;
                    rec["sha256"] = Hash(path);
                    rec["bytes"] = new FileInfo(path).Length;
                    rec["sizes"] = sizes;
                    if (animated)
                    {
                        rec["frames"] = ani.Frames.Count;
                        rec["steps"] = ani.StepCount;
                        rec["seconds"] = Math.Round(ani.TotalJiffies / 60.0, 3);
                        rec["windowsStepTableMatches"] = stepsOk;
                    }
                    rec["hotspotsMatchManifest"] = hotspotsOk;
                    rec["worstMeanRgbError"] = Math.Round(worstMean, 3);
                    rec["worstPixelError"] = worstMax;
                    records.Add(rec);
                    Console.WriteLine("{0,-16} hotspots {1}  mean {2:F3}  max {3,2}{4}", role + ext, hotspotsOk ? "ok" : "BAD", worstMean, worstMax,
                        animated ? string.Format("  {0} frames / {1} steps / {2:F2} s", ani.Frames.Count, ani.StepCount, ani.TotalJiffies / 60.0) : "");
                }
            }

            Dictionary<string, object> styles = new Dictionary<string, object>();
            foreach (string style in new[] { "Lively", "Classic", "Static" })
            {
                List<string> files = new List<string>();
                foreach (string role in Program.Roles)
                {
                    string file = role + (Program.IsAnimated(role, style) ? ".ani" : ".cur");
                    if (!File.Exists(Path.Combine(root, file))) failures.Add("style " + style + " needs missing " + file);
                    files.Add(file);
                }
                styles[style] = files;
            }

            Dictionary<string, object> report = new Dictionary<string, object>();
            report["result"] = failures.Count == 0 ? "passed" : "failed";
            report["method"] = "Each file loaded with LoadImage(LR_LOADFROMFILE) at every stored size, every animation step drawn with DrawIconEx over white and #1A222E, compared with the stored straight-alpha pixels. Hotspots from GetIconInfo; step counts and rates from GetCursorFrameInfo. No system cursor was changed.";
            report["windowsRenders"] = renders;
            report["styles"] = styles;
            report["files"] = records;
            report["failures"] = failures;
            File.WriteAllText(outPath, Pretty(json.Serialize(report)));
            Console.WriteLine("{0}: {1} renders, {2} failures -> {3}", report["result"], renders, failures.Count, outPath);
            foreach (string f in failures) Console.WriteLine("  FAIL " + f);
            return failures.Count == 0 ? 0 : 1;
        }

        static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        // Minimal JSON indenter so reports diff cleanly.
        internal static string Pretty(string compact)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int depth = 0; bool inString = false;
            for (int i = 0; i < compact.Length; i++)
            {
                char c = compact[i];
                if (inString)
                {
                    sb.Append(c);
                    if (c == '\\') { sb.Append(compact[++i]); continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"': inString = true; sb.Append(c); break;
                    case '{': case '[':
                        if (i + 1 < compact.Length && compact[i + 1] == (c == '{' ? '}' : ']')) { sb.Append(c).Append(compact[++i]); break; }
                        sb.Append(c).Append('\n').Append(new string(' ', ++depth * 2)); break;
                    case '}': case ']': sb.Append('\n').Append(new string(' ', --depth * 2)).Append(c); break;
                    case ',': sb.Append(",\n").Append(new string(' ', depth * 2)); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.Append('\n').ToString();
        }
    }
}
