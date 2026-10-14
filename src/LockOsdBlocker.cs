// Suppresses the Acer Quick Access "QA OSD" pop-up for Caps Lock / Num Lock / Scroll Lock.
// AQAUserPS.exe owns the OSD window and re-shows / repositions it for every notification.
// Our low-level keyboard hook runs before AQAUserPS sees the key, so on a lock key press we
// clip the window to an empty region (nothing can be drawn, so no flash) and restore the
// region once the notification is over. As a fallback the window is also hidden whenever it
// is shown or moved during that time. Other Quick Access features and OSDs (mode switching,
// mic-mute, touchpad, Bluelight Shield, ...) keep working.
// Built with uiAccess="true" (AQAUserPS.exe has UIAccess, so only another UIAccess process may
// hide its window); must be signed with a trusted cert and live in Program Files.
// Install / remove with install.ps1 / uninstall.ps1 from an elevated PowerShell.
// Diagnostics: --log writes a trace to %TEMP%\LockOsdBlocker.log; --no-hide / --no-clip
// disable one of the two suppression methods for testing.
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class LockOsdBlocker
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_TIMER = 0x0113;
    const uint EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    const int SW_HIDE = 0;
    const int SuppressMs = 1500; // the OSD notification lasts ~1s

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr hmod, uint tid);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventProc proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("user32.dll")] static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] static extern bool KillTimer(IntPtr hwnd, UIntPtr id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    static LowLevelKeyboardProc keyboardProc = OnKeyboard; // kept in fields so the GC
    static WinEventProc winEventProc = OnWinEvent;         // doesn't collect the callbacks
    static IntPtr osd, clippedOsd;
    static UIntPtr timer;
    static int lastLockKeyTick = int.MinValue / 2;
    static bool logOn;

    static void Log(string s)
    {
        if (logOn)
            System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LockOsdBlocker.log"),
                                         DateTime.Now.ToString("HH:mm:ss.fff ") + s + Environment.NewLine);
    }

    static bool IsOsd(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetWindowText(h, sb, sb.Capacity);
        if (sb.ToString() != "QA OSD") return false;
        sb.Clear();
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString().StartsWith("JUCE_");
    }

    static bool Suppressing() { return unchecked(Environment.TickCount - lastLockKeyTick) <= SuppressMs; }

    static bool noHide, noClip; // test switches
    static void Hide(IntPtr h, string why)
    {
        if (noHide) return;
        if (!IsWindowVisible(h)) return;
        bool r = ShowWindow(h, SW_HIDE);
        Log(why + ": hide ret=" + r + " err=" + Marshal.GetLastWin32Error());
    }

    static IntPtr OnKeyboard(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && ((int)wParam == WM_KEYDOWN || (int)wParam == WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam);
            if (vk == 0x14 || vk == 0x90 || vk == 0x91) // Caps, Num, Scroll Lock
            {
                lastLockKeyTick = Environment.TickCount;
                if (osd == IntPtr.Zero || !IsOsd(osd)) osd = FindWindow(null, "QA OSD");
                if (osd != IntPtr.Zero)
                {
                    if (clippedOsd != osd && !noClip)
                    {
                        int r = SetWindowRgn(osd, CreateRectRgn(0, 0, 0, 0), false); // system owns the region now
                        Log("key " + vk + ": clip ret=" + r + " err=" + Marshal.GetLastWin32Error());
                        if (r != 0) clippedOsd = osd;
                    }
                    Hide(osd, "key " + vk);
                }
                if (timer != UIntPtr.Zero) KillTimer(IntPtr.Zero, timer);
                timer = SetTimer(IntPtr.Zero, UIntPtr.Zero, (uint)SuppressMs, IntPtr.Zero);
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    static void Unclip()
    {
        KillTimer(IntPtr.Zero, timer);
        timer = UIntPtr.Zero;
        if (clippedOsd == IntPtr.Zero) return;
        int r = SetWindowRgn(clippedOsd, IntPtr.Zero, false);
        Log("unclip ret=" + r + " err=" + Marshal.GetLastWin32Error());
        clippedOsd = IntPtr.Zero;
    }

    static void OnWinEvent(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero || !Suppressing()) return;
        if (hwnd != osd)
        {
            if (ev != EVENT_OBJECT_SHOW || !IsOsd(hwnd)) return;
            osd = hwnd;
        }
        Hide(hwnd, ev == EVENT_OBJECT_SHOW ? "show" : "move");
    }

    static void Main(string[] args)
    {
        // A new instance replaces a running one: it signals the quit event, then takes the mutex.
        var quit = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\LockOsdBlocker.quit");
        using (var mutex = new Mutex(false, @"Local\LockOsdBlocker.v3"))
        {
            bool owned;
            try { owned = mutex.WaitOne(0); } catch (AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                quit.Set();
                try { if (!mutex.WaitOne(5000)) return; } catch (AbandonedMutexException) { }
            }
            quit.Reset();
            new Thread(() => { quit.WaitOne(); Environment.Exit(0); }) { IsBackground = true }.Start();
            logOn = Array.IndexOf(args, "--log") >= 0;
            noHide = Array.IndexOf(args, "--no-hide") >= 0;
            noClip = Array.IndexOf(args, "--no-clip") >= 0;
            SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, GetModuleHandle(null), 0);
            SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, winEventProc, 0, 0,
                            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, IntPtr.Zero, winEventProc, 0, 0,
                            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                if (msg.message == WM_TIMER && msg.wParam == (IntPtr)(long)(ulong)timer) Unclip();
        }
    }
}
