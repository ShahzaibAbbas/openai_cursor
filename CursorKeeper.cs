using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

// Current-user cursor replacement. No system file, service, or driver changes.
//
//   CursorKeeper.exe                 tray monitor (started at sign-in)
//   CursorKeeper.exe --apply         apply once and exit
//   CursorKeeper.exe --check         load every asset privately, change nothing, exit 0/1
//   CursorKeeper.exe --style <name>  Lively | Classic | Static; saved and applied
//   CursorKeeper.exe --stop          ask the running monitor to exit
internal static class CursorKeeper
{
    internal const string Scheme = "Soft Black Arrow Persistent";
    internal const string CursorKey = @"Control Panel\Cursors";
    internal const string AccessKey = @"Software\Microsoft\Accessibility";
    internal const string ThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes";
    internal const string ConfigName = "keeper.ini";
    internal static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    internal static readonly string Identity = "Local\\SoftBlackArrowPersistent-" + WindowsIdentity.GetCurrent().User.Value;
    internal static readonly string[] Roles = { "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person" };
    internal static readonly uint[] CursorIds = {32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672};
    // Precision, text, resize and move pointers never animate in any style.
    internal static readonly string[] ClassicAnimated = { "Arrow", "Wait", "AppStarting" };
    internal static readonly string[] LivelyAnimated = { "Arrow", "Wait", "AppStarting", "Help", "Hand", "Pin", "Person", "No" };
    internal static readonly string[] Styles = { "Lively", "Classic", "Static" };
    internal static string LastFingerprint = "";
    internal static string LastSummary = "Not applied yet";
    internal static int Applies = 0;
    internal static DateTime IgnoreBroadcastUntil = DateTime.MinValue;
    private static readonly object LogLock = new object();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetSystemCursor(IntPtr cursor, uint id);
    [DllImport("user32.dll")] private static extern bool DestroyCursor(IntPtr cursor);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr data, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int data, uint flags);
    [DllImport("user32.dll")] private static extern uint SetThreadCursorCreationScaling(uint dpi);

    internal sealed class Options
    {
        public string Style = "Lively";
        public bool FollowWindowsAnimations = true; // "Animation effects" off in Windows => Static
    }

    internal static Options LoadOptions()
    {
        Options o = new Options();
        string path = Path.Combine(Folder, ConfigName);
        if (!File.Exists(path)) return o;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] == '#' || line[0] == ';' || eq < 0) continue;
            string key = line.Substring(0, eq).Trim().ToLowerInvariant(), value = line.Substring(eq + 1).Trim();
            if (key == "style")
            {
                string match = Array.Find(Styles, delegate(string s) { return String.Equals(s, value, StringComparison.OrdinalIgnoreCase); });
                if (match != null) o.Style = match; else Log("Ignoring unknown style '" + value + "' in " + ConfigName);
            }
            else if (key == "followwindowsanimations")
                o.FollowWindowsAnimations = !(value == "0" || String.Equals(value, "false", StringComparison.OrdinalIgnoreCase));
        }
        return o;
    }

    internal static void SaveOptions(Options o)
    {
        string path = Path.Combine(Folder, ConfigName), temp = path + ".tmp";
        File.WriteAllText(temp,
            "# Soft Black Arrow cursor keeper settings\r\n" +
            "# style: Lively (8 animated pointers), Classic (Normal, Busy, Working), Static (none)\r\n" +
            "style=" + o.Style + "\r\n" +
            "# true: use Static while Windows 'Animation effects' is turned off\r\n" +
            "followWindowsAnimations=" + (o.FollowWindowsAnimations ? "true" : "false") + "\r\n", new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }

    internal static bool WindowsAnimationsEnabled()
    {
        int enabled;
        // SPI_GETCLIENTAREAANIMATION: Settings > Accessibility > Visual effects > Animation effects
        return !SystemParametersInfo(0x1042, 0, out enabled, 0) || enabled != 0;
    }

    internal static string EffectiveStyle(Options o)
    {
        return o.FollowWindowsAnimations && !WindowsAnimationsEnabled() ? "Static" : o.Style;
    }

    internal static bool Animates(string role, string style)
    {
        if (style == "Lively") return Array.IndexOf(LivelyAnimated, role) >= 0;
        if (style == "Classic") return Array.IndexOf(ClassicAnimated, role) >= 0;
        return false;
    }

    internal static string Asset(string role, string style)
    {
        string animated = Path.Combine(Folder, role + ".ani");
        if (Animates(role, style) && File.Exists(animated)) return animated;
        return Path.Combine(Folder, role + ".cur");
    }

    internal static object Read(string key, string name, object fallback)
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(key))
            return k == null ? fallback : k.GetValue(name, fallback);
    }

    private static void SetIfDifferent(string key, string name, object value, RegistryValueKind kind)
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(key))
        {
            if (!Object.Equals(k.GetValue(name, null), value)) k.SetValue(name, value, kind);
        }
    }

    internal static string Fingerprint(string style)
    {
        List<string> values = new List<string>();
        values.Add(style);
        foreach (string r in Roles) values.Add(Convert.ToString(Read(CursorKey,r,"")));
        foreach (string n in new [] { "", "Scheme Source", "CursorBaseSize" }) values.Add(Convert.ToString(Read(CursorKey,n,"")));
        foreach (string n in new [] { "CursorType", "CursorSize", "CursorColor" }) values.Add(Convert.ToString(Read(AccessKey,n,"")));
        values.Add(Convert.ToString(Read(ThemeKey,"ThemeChangesMousePointers","")));
        values.Add(Convert.ToString(Read(CursorKey + @"\Schemes",Scheme,"")));
        return String.Join("|", values);
    }

    internal static void Log(string message)
    {
        lock (LogLock)
        {
            try
            {
                string path = Path.Combine(Folder,"keeper.log");
                if (File.Exists(path) && new FileInfo(path).Length > 131072)
                    File.WriteAllText(path,"Log rotated\r\n");
                File.AppendAllText(path,DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
            }
            catch { }
        }
    }

    private static int CursorBaseSize()
    {
        int size = Convert.ToInt32(Read(CursorKey,"CursorBaseSize",32));
        if (size < 16 || size > 512) throw new InvalidDataException("CursorBaseSize is outside the supported range.");
        return size;
    }

    // Explicit logical sizing: default-size LoadImage returns 32px even when the user's base is 48px.
    private static IntPtr LoadSized(string path, int size)
    {
        uint oldDpi = 0;
        try { oldDpi = SetThreadCursorCreationScaling(96); } catch (EntryPointNotFoundException) { }
        try { return LoadImage(IntPtr.Zero,path,2,size,size,0x10); }
        finally { if (oldDpi != 0) try { SetThreadCursorCreationScaling(oldDpi); } catch (EntryPointNotFoundException) { } }
    }

    // Loads every asset of the effective style into private handles. Nothing is applied.
    internal static List<string> Check(out string summary)
    {
        List<string> problems = new List<string>();
        Options options = LoadOptions();
        string style = EffectiveStyle(options);
        int size = 32;
        try { size = CursorBaseSize(); } catch (Exception ex) { problems.Add(ex.Message); }
        int animated = 0;
        foreach (string r in Roles)
        {
            string path = Asset(r, style);
            if (!File.Exists(path)) { problems.Add("Missing " + Path.GetFileName(path)); continue; }
            if (path.EndsWith(".ani", StringComparison.OrdinalIgnoreCase)) animated++;
            foreach (int s in new [] { size, 32 })
            {
                IntPtr cursor = LoadSized(path, s);
                if (cursor == IntPtr.Zero) problems.Add("Windows could not load " + Path.GetFileName(path) + " at " + s + "px (error " + Marshal.GetLastWin32Error() + ")");
                else DestroyCursor(cursor);
            }
        }
        summary = "Style " + style + (style != options.Style ? " (Windows animation effects are off; configured " + options.Style + ")" : "") +
                  ", " + size + "px, " + animated + " animated of " + Roles.Length + " roles";
        return problems;
    }

    internal static bool Apply(bool force, string reason)
    {
        Options options = LoadOptions();
        string style = EffectiveStyle(options);
        if (!force && Fingerprint(style) == LastFingerprint) return false;
        using (Mutex gate = new Mutex(false,Identity + "-apply"))
        {
            bool held = false;
            try
            {
                try { held = gate.WaitOne(3000); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new IOException("Another cursor apply operation is still running.");
                int size = CursorBaseSize();
                // Preserve CursorBaseSize, CursorSize and CursorColor exactly as selected by the user.
                foreach (string r in Roles)
                    if (!File.Exists(Asset(r, style))) throw new FileNotFoundException("Missing cursor asset",Asset(r, style));

                List<string> paths = new List<string>();
                foreach (string r in Roles)
                {
                    string path = Asset(r, style);
                    paths.Add(path);
                    SetIfDifferent(CursorKey,r,path,RegistryValueKind.ExpandString);
                }
                SetIfDifferent(CursorKey,"",Scheme,RegistryValueKind.String);
                SetIfDifferent(CursorKey,"Scheme Source",1,RegistryValueKind.DWord);
                SetIfDifferent(CursorKey + @"\Schemes",Scheme,String.Join(",",paths),RegistryValueKind.String);
                SetIfDifferent(AccessKey,"CursorType",0,RegistryValueKind.DWord);
                SetIfDifferent(ThemeKey,"ThemeChangesMousePointers",0,RegistryValueKind.DWord);

                IgnoreBroadcastUntil = DateTime.UtcNow.AddMilliseconds(900);
                if (!SystemParametersInfo(0x57,0,IntPtr.Zero,0))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Windows cursor reload failed.");

                List<string> replaced = new List<string>();
                List<string> registryOnly = new List<string>();
                List<string> animated = new List<string>();
                for (int i=0;i<Roles.Length;i++)
                {
                    string path = Asset(Roles[i], style);
                    if (path.EndsWith(".ani", StringComparison.OrdinalIgnoreCase)) animated.Add(Roles[i]);
                    IntPtr cursor = LoadSized(path,size);
                    if (cursor == IntPtr.Zero) throw new IOException("Windows could not load " + Roles[i] + " at " + size + "px.");
                    if (SetSystemCursor(cursor,CursorIds[i]))
                        replaced.Add(Roles[i]); // SetSystemCursor takes ownership on success.
                    else
                    {
                        DestroyCursor(cursor);
                        if (Roles[i] != "NWPen" && Roles[i] != "Pin" && Roles[i] != "Person")
                            throw new IOException("Windows could not replace the system cursor for " + Roles[i]);
                        registryOnly.Add(Roles[i]);
                    }
                }
                LastFingerprint = Fingerprint(style);
                Applies++;
                LastSummary = style + " \u00b7 " + size + "px \u00b7 " + replaced.Count + " native roles";
                var status = new {
                    scheme = Scheme, folder = Folder, pid = Process.GetCurrentProcess().Id,
                    appliedUtc = DateTime.UtcNow.ToString("o"), reason = reason,
                    style = style, configuredStyle = options.Style, followWindowsAnimations = options.FollowWindowsAnimations,
                    cursorBaseSize = size, accessibilitySize = Read(AccessKey,"CursorSize",null),
                    applyCount = Applies, nativeRoles = replaced.ToArray(), registryOnlyRoles = registryOnly.ToArray(),
                    animatedRoles = animated.ToArray()
                };
                File.WriteAllText(Path.Combine(Folder,"state.json"),new JavaScriptSerializer().Serialize(status));
                Log("Applied " + style + " " + size + "px; " + replaced.Count + " native roles, " + animated.Count + " animated; " + reason);
                return true;
            }
            finally { if (held) gate.ReleaseMutex(); }
        }
    }

    // Saves a new style and applies it: through the running monitor when there is one.
    internal static void ChangeStyle(string style, bool? follow, string reason)
    {
        Options o = LoadOptions();
        if (style != null) o.Style = style;
        if (follow.HasValue) o.FollowWindowsAnimations = follow.Value;
        SaveOptions(o);
        try
        {
            using (EventWaitHandle e = EventWaitHandle.OpenExisting(Identity + "-reapply")) { e.Set(); return; }
        }
        catch (WaitHandleCannotBeOpenedException) { }
        Apply(true, reason);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string command = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (command == "--stop")
            {
                try { using (EventWaitHandle e = EventWaitHandle.OpenExisting(Identity + "-stop")) e.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            if (command == "--apply") { Apply(true,"one-shot apply"); return 0; }
            if (command == "--check")
            {
                string summary;
                List<string> problems = Check(out summary);
                Log("Check: " + summary + (problems.Count == 0 ? "; all assets load" : "; " + String.Join("; ", problems)));
                return problems.Count == 0 ? 0 : 1;
            }
            if (command == "--style")
            {
                string wanted = args.Length > 1 ? Array.Find(Styles, delegate(string s) { return String.Equals(s, args[1], StringComparison.OrdinalIgnoreCase); }) : null;
                if (wanted == null) { Log("ERROR --style needs one of: " + String.Join(", ", Styles)); return 2; }
                ChangeStyle(wanted, null, "style set to " + wanted);
                return 0;
            }
            if (command.Length > 0) { Log("ERROR unknown argument " + args[0]); return 2; }
            bool fresh;
            using (Mutex instance = new Mutex(true,Identity + "-monitor",out fresh))
            {
                if (!fresh) return 0;
                Application.EnableVisualStyles();
                using (KeeperContext context = new KeeperContext()) Application.Run(context);
                instance.ReleaseMutex();
            }
            return 0;
        }
        catch (Exception ex) { Log("ERROR " + ex.ToString()); return 1; }
    }
}

// Waits on registry change notifications for one key. If the key is missing or is
// deleted and recreated, the watcher re-opens it instead of silently stopping.
internal sealed class RegistryWatch : IDisposable
{
    [DllImport("advapi32.dll",SetLastError=true)]
    private static extern int RegNotifyChangeKeyValue(IntPtr key,bool subtree,uint filter,IntPtr evt,bool asynchronous);
    private readonly ManualResetEvent closed = new ManualResetEvent(false);
    private readonly object sync = new object();
    private RegistryKey key;
    private volatile bool closing;

    internal RegistryWatch(string path,Action changed)
    {
        Thread thread = new Thread(delegate() {
            while (!closing)
            {
                try
                {
                    RegistryKey opened = Registry.CurrentUser.OpenSubKey(path);
                    lock (sync)
                    {
                        if (closing) { if (opened != null) opened.Close(); break; }
                        key = opened;
                    }
                    if (opened == null) { closed.WaitOne(5000); continue; }
                    while (!closing)
                    {
                        int result = RegNotifyChangeKeyValue(opened.Handle.DangerousGetHandle(),false,4,IntPtr.Zero,false);
                        if (closing) break;
                        if (result != 0) { CursorKeeper.Log("Watcher for " + path + " restarting (error " + result + ")"); break; }
                        changed();
                    }
                }
                catch (ObjectDisposedException) { if (closing) break; }
                catch (Exception ex) { CursorKeeper.Log("Watcher error: " + ex.Message); }
                lock (sync) { if (key != null) { key.Close(); key = null; } }
                if (!closing) closed.WaitOne(2000);
            }
        });
        thread.IsBackground=true;
        thread.Start();
    }

    public void Dispose()
    {
        closing=true;
        closed.Set();
        lock (sync) { if (key!=null) { key.Close(); key = null; } }
    }
}

internal sealed class NotificationWindow : NativeWindow, IDisposable
{
    private readonly Action<string,bool> notify;
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(IntPtr window,uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(IntPtr window);
    internal NotificationWindow(Action<string,bool> notify)
    {
        this.notify=notify;
        // Hidden top-level windows receive broadcasts. HWND_MESSAGE windows do not.
        CreateHandle(new CreateParams { Caption="Soft Black Arrow settings listener",Style=unchecked((int)0x80000000) });
        WTSRegisterSessionNotification(Handle,0);
    }
    protected override void WndProc(ref Message m)
    {
        // WM_SETTINGCHANGE (includes the Windows animation-effects toggle), WM_THEMECHANGED,
        // WM_DISPLAYCHANGE, WM_POWERBROADCAST, WM_WTSSESSION_CHANGE
        if (m.Msg==0x1A || m.Msg==0x31A || m.Msg==0x7E || m.Msg==0x218 || m.Msg==0x2B1)
        {
            if (DateTime.UtcNow >= CursorKeeper.IgnoreBroadcastUntil)
                notify("Windows setting/display/session notification",true);
        }
        base.WndProc(ref m);
    }
    public void Dispose() { WTSUnRegisterSessionNotification(Handle); DestroyHandle(); }
}

internal sealed class KeeperContext : ApplicationContext, IDisposable
{
    private readonly Control dispatch = new Control();
    private readonly List<RegistryWatch> watches = new List<RegistryWatch>();
    private readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer();
    private readonly System.Windows.Forms.Timer startup = new System.Windows.Forms.Timer();
    private readonly EventWaitHandle stop = new EventWaitHandle(false,EventResetMode.ManualReset,CursorKeeper.Identity + "-stop");
    private readonly EventWaitHandle reapply = new EventWaitHandle(false,EventResetMode.AutoReset,CursorKeeper.Identity + "-reapply");
    private readonly RegisteredWaitHandle stopRegistration;
    private readonly RegisteredWaitHandle reapplyRegistration;
    private readonly NotificationWindow window;
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly Dictionary<string,ToolStripMenuItem> styleItems = new Dictionary<string,ToolStripMenuItem>();
    private readonly ToolStripMenuItem followItem;
    private string reason="settings change";
    private bool force;
    private int startupPasses;
    private volatile bool closing;

    internal KeeperContext()
    {
        IntPtr unused=dispatch.Handle;
        stop.Reset();
        debounce.Interval=450;
        debounce.Tick+=delegate { debounce.Stop(); bool f=force; force=false; Reapply(f,reason); };
        window=new NotificationWindow(Schedule);
        stopRegistration=ThreadPool.RegisterWaitForSingleObject(stop,delegate { if(!closing) dispatch.BeginInvoke(new Action(ExitThread)); },null,Timeout.Infinite,true);
        reapplyRegistration=ThreadPool.RegisterWaitForSingleObject(reapply,delegate { Schedule("style changed",true); },null,Timeout.Infinite,false);

        ContextMenuStrip menu=new ContextMenuStrip();
        ToolStripMenuItem header=new ToolStripMenuItem("Soft Black Arrow") { Enabled=false };
        header.Font=new Font(header.Font,FontStyle.Bold);
        menu.Items.Add(header);
        menu.Items.Add(new ToolStripSeparator());
        ToolStripMenuItem animation=new ToolStripMenuItem("Animation");
        foreach (string[] s in new [] {
            new [] { "Lively", "Lively \u2014 8 animated pointers" },
            new [] { "Classic", "Classic \u2014 Normal, Busy and Working" },
            new [] { "Static", "Static \u2014 no animation" } })
        {
            string style=s[0];
            ToolStripMenuItem item=new ToolStripMenuItem(s[1]);
            item.Click+=delegate { ChangeStyle(style,null); };
            styleItems[style]=item;
            animation.DropDownItems.Add(item);
        }
        animation.DropDownItems.Add(new ToolStripSeparator());
        followItem=new ToolStripMenuItem("Pause when Windows animation effects are off");
        followItem.Click+=delegate { ChangeStyle(null,!CursorKeeper.LoadOptions().FollowWindowsAnimations); };
        animation.DropDownItems.Add(followItem);
        menu.Items.Add(animation);
        menu.Items.Add("Reapply cursor scheme",null,delegate { Reapply(true,"tray reapply"); });
        menu.Items.Add("Open cursor folder",null,delegate { Process.Start("explorer.exe",'"'+CursorKeeper.Folder+'"'); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Stop protection (until next sign-in)",null,delegate { ExitThread(); });
        menu.Opening+=delegate { RefreshMenu(); };

        icon=LoadIcon();
        tray=new NotifyIcon { Icon=icon,Text="Soft Black Arrow - cursor protection",ContextMenuStrip=menu,Visible=true };
        tray.DoubleClick+=delegate { tray.ShowBalloonTip(2500,"Soft Black Arrow",CursorKeeper.LastSummary,ToolTipIcon.None); };
        Reapply(true,"monitor startup");
        // Watch after the first apply so the Schemes key exists on a fresh profile.
        foreach(string key in new [] {CursorKeeper.CursorKey,CursorKeeper.AccessKey,CursorKeeper.ThemeKey,CursorKeeper.CursorKey+@"\Schemes"})
            watches.Add(new RegistryWatch(key,delegate { Schedule("cursor registry changed",false); }));
        startup.Interval=1800;
        startup.Tick+=delegate { Reapply(true,"startup settling"); if(++startupPasses==3) startup.Stop(); };
        startup.Start();
    }

    private static Icon LoadIcon()
    {
        try
        {
            using (Stream s=typeof(KeeperContext).Assembly.GetManifestResourceStream("CursorKeeper.ico"))
                if (s!=null) return new Icon(s,SystemInformation.SmallIconSize);
        }
        catch (Exception ex) { CursorKeeper.Log("Icon fallback: " + ex.Message); }
        return (Icon)SystemIcons.Application.Clone();
    }

    private void RefreshMenu()
    {
        CursorKeeper.Options o=CursorKeeper.LoadOptions();
        string effective=CursorKeeper.EffectiveStyle(o);
        foreach (KeyValuePair<string,ToolStripMenuItem> pair in styleItems) pair.Value.Checked = pair.Key==o.Style;
        followItem.Checked=o.FollowWindowsAnimations;
        followItem.Text="Pause when Windows animation effects are off" + (effective!=o.Style ? " (paused now)" : "");
    }

    private void ChangeStyle(string style,bool? follow)
    {
        try { CursorKeeper.ChangeStyle(style,follow,"tray: " + (style ?? "follow Windows animations toggled")); }
        catch(Exception ex) { CursorKeeper.Log("ERROR " + ex.Message); }
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        string text="Soft Black Arrow \u00b7 " + CursorKeeper.LastSummary;
        tray.Text=text.Length > 63 ? text.Substring(0,63) : text;
    }

    private void Reapply(bool forced,string why)
    {
        try { CursorKeeper.Apply(forced,why); }
        catch(Exception ex) { CursorKeeper.Log("ERROR " + ex.Message); }
        if (tray!=null) UpdateTooltip();
    }
    private void Schedule(string why,bool forced)
    {
        if(closing)return;
        if(dispatch.InvokeRequired) { dispatch.BeginInvoke(new Action(delegate { Schedule(why,forced); })); return; }
        reason=why; force|=forced;
        debounce.Stop(); debounce.Start();
    }
    protected override void ExitThreadCore()
    {
        closing=true;
        debounce.Stop(); startup.Stop();
        tray.Visible=false;
        CursorKeeper.Log("Monitor stopped");
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            closing=true;
            stopRegistration.Unregister(null);
            reapplyRegistration.Unregister(null);
            foreach(RegistryWatch watch in watches)watch.Dispose();
            window.Dispose();tray.Dispose();icon.Dispose();debounce.Dispose();startup.Dispose();stop.Dispose();reapply.Dispose();dispatch.Dispose();
        }
        base.Dispose(disposing);
    }
}
