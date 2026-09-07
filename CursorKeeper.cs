using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

// Current-user cursor replacement. No system file, service, or driver changes.
internal static class CursorKeeper
{
    internal const string Scheme = "Soft Black Arrow Persistent";
    internal const string CursorKey = @"Control Panel\Cursors";
    internal const string AccessKey = @"Software\Microsoft\Accessibility";
    internal const string ThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes";
    internal static readonly string Folder = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    internal static readonly string Identity = "Local\\SoftBlackArrowPersistent-" + WindowsIdentity.GetCurrent().User.Value;
    internal static readonly string[] Roles = { "Arrow", "Help", "AppStarting", "Wait", "Crosshair", "IBeam", "NWPen", "No", "SizeNS", "SizeWE", "SizeNWSE", "SizeNESW", "SizeAll", "UpArrow", "Hand", "Pin", "Person" };
    internal static readonly uint[] CursorIds = {32512,32651,32650,32514,32515,32513,32631,32648,32645,32644,32642,32643,32646,32516,32649,32671,32672};
    internal static string LastFingerprint = "";
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
    [DllImport("user32.dll")] private static extern uint SetThreadCursorCreationScaling(uint dpi);

    internal static string Asset(string role)
    {
        return Path.Combine(Folder, role + ((role == "Arrow" || role == "Wait" || role == "AppStarting") ? ".ani" : ".cur"));
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

    internal static string Fingerprint()
    {
        List<string> values = new List<string>();
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

    internal static bool Apply(bool force, string reason)
    {
        if (!force && Fingerprint() == LastFingerprint) return false;
        using (Mutex gate = new Mutex(false,Identity + "-apply"))
        {
            bool held = false;
            try
            {
                try { held = gate.WaitOne(3000); } catch (AbandonedMutexException) { held = true; }
                if (!held) throw new IOException("Another cursor apply operation is still running.");
                int size = Convert.ToInt32(Read(CursorKey,"CursorBaseSize",32));
                if (size < 16 || size > 512) throw new InvalidDataException("CursorBaseSize is outside the supported range.");
                // Preserve CursorBaseSize, CursorSize and CursorColor exactly as selected by the user.
                foreach (string r in Roles)
                    if (!File.Exists(Asset(r))) throw new FileNotFoundException("Missing cursor asset",Asset(r));

                List<string> paths = new List<string>();
                foreach (string r in Roles)
                {
                    string path = Asset(r);
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

                // Explicit logical sizing: default-size LoadImage returns 32px even when the user's base is 48px.
                uint oldDpi = 0;
                try { oldDpi = SetThreadCursorCreationScaling(96); } catch (EntryPointNotFoundException) { }
                List<string> replaced = new List<string>();
                List<string> registryOnly = new List<string>();
                try
                {
                    for (int i=0;i<Roles.Length;i++)
                    {
                        IntPtr cursor = LoadImage(IntPtr.Zero,Asset(Roles[i]),2,size,size,0x10);
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
                }
                finally { if (oldDpi != 0) try { SetThreadCursorCreationScaling(oldDpi); } catch (EntryPointNotFoundException) { } }
                LastFingerprint = Fingerprint();
                Applies++;
                var status = new {
                    scheme = Scheme, folder = Folder, pid = Process.GetCurrentProcess().Id,
                    appliedUtc = DateTime.UtcNow.ToString("o"), reason = reason,
                    cursorBaseSize = size, accessibilitySize = Read(AccessKey,"CursorSize",null),
                    applyCount = Applies, nativeRoles = replaced.ToArray(), registryOnlyRoles = registryOnly.ToArray()
                };
                File.WriteAllText(Path.Combine(Folder,"state.json"),new JavaScriptSerializer().Serialize(status));
                Log("Applied " + size + "px; " + replaced.Count + " native roles; " + reason);
                return true;
            }
            finally { if (held) gate.ReleaseMutex(); }
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--stop")
            {
                try { using (EventWaitHandle e = EventWaitHandle.OpenExisting(Identity + "-stop")) e.Set(); }
                catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--apply") { Apply(true,"one-shot apply"); return 0; }
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

internal sealed class RegistryWatch : IDisposable
{
    [DllImport("advapi32.dll",SetLastError=true)]
    private static extern int RegNotifyChangeKeyValue(IntPtr key,bool subtree,uint filter,IntPtr evt,bool asynchronous);
    private readonly RegistryKey key;
    private volatile bool closing;
    internal RegistryWatch(string path,Action changed)
    {
        key = Registry.CurrentUser.OpenSubKey(path);
        if (key == null) return;
        Thread thread = new Thread(delegate() {
            try
            {
                while (!closing)
                {
                    int result = RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(),false,4,IntPtr.Zero,false);
                    if (result != 0 || closing) break;
                    changed();
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { CursorKeeper.Log("Watcher error: " + ex.Message); }
        });
        thread.IsBackground=true;
        thread.Start();
    }
    public void Dispose() { closing=true; if (key!=null) key.Close(); }
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
    private readonly RegisteredWaitHandle stopRegistration;
    private readonly NotificationWindow window;
    private readonly NotifyIcon tray;
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
        foreach(string key in new [] {CursorKeeper.CursorKey,CursorKeeper.AccessKey,CursorKeeper.ThemeKey,CursorKeeper.CursorKey+@"\Schemes"})
            watches.Add(new RegistryWatch(key,delegate { Schedule("cursor registry changed",false); }));
        stopRegistration=ThreadPool.RegisterWaitForSingleObject(stop,delegate { if(!closing) dispatch.BeginInvoke(new Action(ExitThread)); },null,Timeout.Infinite,true);
        ContextMenuStrip menu=new ContextMenuStrip();
        menu.Items.Add("Reapply cursor scheme",null,delegate { Reapply(true,"tray reapply"); });
        menu.Items.Add("Open cursor folder",null,delegate { Process.Start("explorer.exe",'"'+CursorKeeper.Folder+'"'); });
        menu.Items.Add("Stop protection (until next sign-in)",null,delegate { ExitThread(); });
        tray=new NotifyIcon { Icon=SystemIcons.Application,Text="Soft Black Arrow - cursor protection",ContextMenuStrip=menu,Visible=true };
        Reapply(true,"monitor startup");
        startup.Interval=1800;
        startup.Tick+=delegate { Reapply(true,"startup settling"); if(++startupPasses==3) startup.Stop(); };
        startup.Start();
    }
    private void Reapply(bool forced,string why)
    {
        try { CursorKeeper.Apply(forced,why); }
        catch(Exception ex) { CursorKeeper.Log("ERROR " + ex.Message); }
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
            foreach(RegistryWatch watch in watches)watch.Dispose();
            window.Dispose();tray.Dispose();debounce.Dispose();startup.Dispose();stop.Dispose();dispatch.Dispose();
        }
        base.Dispose(disposing);
    }
}
