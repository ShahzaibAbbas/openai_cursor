using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

// CursorForge: rebuilds the derived Soft Black Arrow assets from the static artwork.
//   build   <root>             generate the Lively .ani files and losslessly repack v3 ones
//   verify  <root> <out.json>  load every asset through Windows and check it
//   web     <root> <outDir>    sprite sheets + timeline JSON for docs/index.html
//   gif     <root> <out.gif>   animated README preview of the Lively effects
//   icon    <root> <out.ico>   application/tray icon from the Arrow artwork
//   scheme  <root> <out.png>   overview of all 17 roles (scheme-preview.png)
//   sheet   <root> <out.png> <role> [steps]  contact sheet for eyeballing frames
namespace CursorForge
{
    internal static class Program
    {
        internal static readonly string[] Roles = { "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person" };
        internal static readonly string[] ClassicAnimated = { "Arrow", "Wait", "AppStarting" };
        internal static readonly Color Light = Color.FromArgb(255, 255, 255);
        internal static readonly Color Dark = Color.FromArgb(26, 34, 46);

        private static int Main(string[] args)
        {
            try
            {
                CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
                System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                if (args.Length < 2) { Console.Error.WriteLine("usage: CursorForge build|verify|web|gif|icon|sheet <root> ..."); return 2; }
                string root = Path.GetFullPath(args[1]);
                switch (args[0])
                {
                    case "build": Build(root); break;
                    case "verify": return Verifier.Run(root, args[2]);
                    case "web": Web.Export(root, args[2]); break;
                    case "gif": Previews.Gif(root, args[2]); break;
                    case "icon": Previews.Icon(root, args[2]); break;
                    case "scheme": Previews.Scheme(root, args[2]); break;
                    case "sheet": Sheet(root, args[2], args[3], args.Length > 4 ? args[4] : null, args.Length > 5 ? int.Parse(args[5]) : 128, args.Length > 6 ? int.Parse(args[6]) : 1); break;
                    default: Console.Error.WriteLine("unknown command " + args[0]); return 2;
                }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("ERROR " + ex); return 1; }
        }

        private static void Build(string root)
        {
            Renderer renderer = new Renderer(root);
            foreach (Recipe r in Animations.All())
            {
                AniFile ani = renderer.Build(r);
                string path = Path.Combine(root, r.Role + ".ani");
                ani.Save(path);
                Console.WriteLine("{0,-12} {1,2} frames, {2,2} steps, {3:F2} s, {4,10:N0} bytes", r.Role + ".ani", ani.Frames.Count, ani.StepCount, ani.TotalJiffies / 60.0, new FileInfo(path).Length);
            }
            foreach (string role in ClassicAnimated)
            {
                string path = Path.Combine(root, role + ".ani");
                AniFile source = AniFile.Read(path);
                AniFile packed = Packer.Repack(source);
                if (packed.Frames.Count < source.Frames.Count)
                {
                    long before = new FileInfo(path).Length;
                    packed.Save(path);
                    Console.WriteLine("{0,-12} repacked {1} -> {2} unique frames, {3:N0} -> {4:N0} bytes", role + ".ani", source.Frames.Count, packed.Frames.Count, before, new FileInfo(path).Length);
                }
                else Console.WriteLine("{0,-12} already minimal ({1} unique frames)", role + ".ani", source.Frames.Count);
            }
        }

        // Grid of animation steps at one size (rows: light, dark) for visual review.
        private static void Sheet(string root, string outPath, string role, string stepList, int size, int zoom)
        {
            string path = Path.Combine(root, role + ".ani");
            AniFile ani = AniFile.Read(path);
            List<int> steps = new List<int>();
            if (stepList != null && stepList != "all") foreach (string s in stepList.Split(',')) steps.Add(int.Parse(s));
            else for (int i = 0; i < ani.StepCount; i++) steps.Add(i);
            int cell = size * zoom, pad = 8;
            using (Bitmap b = new Bitmap(steps.Count * (cell + pad) + pad, 2 * (cell + pad) + pad))
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.Clear(Light);
                using (SolidBrush dark = new SolidBrush(Dark)) g.FillRectangle(dark, 0, cell + pad + pad / 2, b.Width, b.Height);
                // Rendered by Windows itself, so this shows exactly what the pointer looks like.
                Img[] light = WindowsCursor.RenderSteps(path, size, steps.ToArray(), Light);
                Img[] dark2 = WindowsCursor.RenderSteps(path, size, steps.ToArray(), Dark);
                for (int i = 0; i < steps.Count; i++)
                    using (Bitmap l = light[i].ToBitmap())
                    using (Bitmap d = dark2[i].ToBitmap())
                    {
                        g.DrawImage(l, pad + i * (cell + pad), pad, cell, cell);
                        g.DrawImage(d, pad + i * (cell + pad), cell + 2 * pad, cell, cell);
                    }
                b.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        internal static bool IsAnimated(string role, string style)
        {
            if (style == "Static") return false;
            if (style == "Classic") return Array.IndexOf(ClassicAnimated, role) >= 0;
            return Array.IndexOf(ClassicAnimated, role) >= 0 || Animations.Find(role) != null;
        }
    }
}
