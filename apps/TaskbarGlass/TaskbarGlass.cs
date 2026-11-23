using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

static class TaskbarGlass
{
    enum Accent { Disabled = 0, Gradient = 1, Transparent = 2, BlurBehind = 3, Acrylic = 4 }

    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy { public int State; public int Flags; public uint Color; public int AnimationId; }

    [StructLayout(LayoutKind.Sequential)]
    struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct PowerStatus { public byte AC, Battery, Percent, SaverFlag; public int LifeTime, FullLifeTime; }

    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hwnd, IntPtr rgn, bool redraw);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int RegisterWindowMessage(string msg);
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, int param, ref bool value, int ini);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out PowerStatus s);

    const string RegPath = @"Software\TaskbarGlass";
    const double CycleSeconds = 16;

    static Accent mode = Accent.Acrylic;
    static bool floating = true, animate = true, colorCycle = true, alerts = true, followWindows = false, autoHide = true, iconsOnly = true;

    enum Theme { Wallpaper = 0, StrawHat = 1, Gear5 = 2 }
    static Theme theme = Theme.StrawHat;

    static int[][] palette =
    {
        new[] { 16, 20, 48 }, new[] { 52, 30, 96 }, new[] { 96, 38, 72 }, new[] { 22, 44, 110 },
    };

    // Straw Hat: hat straw gold, vest red, open-sea blue, sunset orange
    static readonly int[][] StrawHatPalette =
    {
        new[] { 128, 92, 12 }, new[] { 140, 18, 26 }, new[] { 12, 58, 128 }, new[] { 156, 62, 16 },
    };

    // Gear 5: drum-beat lavender and cloud white, kept dark enough for white icons
    static readonly int[][] Gear5Palette =
    {
        new[] { 84, 62, 136 }, new[] { 118, 100, 168 }, new[] { 62, 42, 112 }, new[] { 136, 116, 178 },
    };

    static int[][] ActivePalette()
    {
        if (theme == Theme.StrawHat) return StrawHatPalette;
        if (theme == Theme.Gear5) return Gear5Palette;
        return palette;
    }

    // Attention glow colour: haki red for Straw Hat, bright cloud white for Gear 5, amber otherwise
    static int[] PulseColour()
    {
        if (theme == Theme.StrawHat) return new[] { 220, 24, 36 };
        if (theme == Theme.Gear5) return new[] { 240, 236, 255 };
        return new[] { 230, 120, 40 };
    }

    static bool Rubber { get { return theme != Theme.Wallpaper; } }

    static bool lightTheme;

    class Bar
    {
        public double Hover, Dock, Side = double.NaN, SideVel, Squash, SquashVel;
        public int LastSide = -1, LastBottom = -1, LastTop = -1, LastRadius = -1, LastW, LastH;
        public Overlay Layer;
        public List<Rectangle> Groups;
        public string GroupKey;
        public double LastScan = -10;
        public double LastAccent = -10;
        public bool WasOver;
        public List<Rectangle> Items;
        public Rectangle Hovered;
        public double LastAutoHideCheck = -10;
        public int LastLeft = int.MinValue;
        public bool HiddenRegion;
        public List<Rectangle> Shapes;
        public double PopStart = -1;
        public volatile bool Scanning;
        public volatile object[] Pending;
    }
    static readonly Dictionary<IntPtr, Bar> bars = new Dictionary<IntPtr, Bar>();
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static double lastTick, pulse, pulseStart = -10, sweepStart = -10;
    static System.Windows.Forms.Timer timer;
    static StreamWriter debugLog;

    // ---------- settings ----------

    static bool ReadBool(RegistryKey k, string name, bool fallback)
    {
        object v = k == null ? null : k.GetValue(name);
        return v == null ? fallback : (int)v != 0;
    }

    static void LoadSettings()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath))
        {
            floating = ReadBool(k, "Floating", true);
            autoHide = ReadBool(k, "AutoHide", true);
            iconsOnly = ReadBool(k, "IconsOnly", true);
            animate = ReadBool(k, "Animate", true);
            colorCycle = ReadBool(k, "ColourDrift", true);
            alerts = ReadBool(k, "Alerts", true);
            followWindows = ReadBool(k, "FollowWindows", false);
            mode = ReadBool(k, "Clear", false) ? Accent.BlurBehind : Accent.Acrylic;
            object t = k == null ? null : k.GetValue("Theme");
            theme = t == null ? Theme.StrawHat : (Theme)(int)t;
        }
    }

    static void SaveSettings()
    {
        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegPath))
        {
            k.SetValue("Floating", floating ? 1 : 0);
            k.SetValue("AutoHide", autoHide ? 1 : 0);
            k.SetValue("IconsOnly", iconsOnly ? 1 : 0);
            k.SetValue("Animate", animate ? 1 : 0);
            k.SetValue("ColourDrift", colorCycle ? 1 : 0);
            k.SetValue("Alerts", alerts ? 1 : 0);
            k.SetValue("FollowWindows", followWindows ? 1 : 0);
            k.SetValue("Clear", mode == Accent.BlurBehind ? 1 : 0);
            k.SetValue("Theme", (int)theme);
        }
    }

    static string StartupLink
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Taskbar Glass.lnk"); }
    }

    static void SetStartup(bool on)
    {
        if (!on) { if (File.Exists(StartupLink)) File.Delete(StartupLink); return; }
        Type t = Type.GetTypeFromProgID("WScript.Shell");
        dynamic shell = Activator.CreateInstance(t);
        dynamic link = shell.CreateShortcut(StartupLink);
        link.TargetPath = Application.ExecutablePath;
        link.WorkingDirectory = Path.GetDirectoryName(Application.ExecutablePath);
        link.Save();
    }

    // ---------- environment ----------

    static void ReadTheme()
    {
        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
        {
            object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
            lightTheme = v != null && (int)v == 1;
        }
    }

    // Builds the drift palette from the bottom of the current wallpaper, so the glass always matches it.
    static void ReadWallpaperPalette()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
            if (!File.Exists(path)) return;
            using (var src = new Bitmap(new MemoryStream(File.ReadAllBytes(path))))
            using (var small = new Bitmap(src, new Size(64, 36)))
            {
                // Four horizontal bands across the full width pick up every colour layer in the wallpaper.
                var result = new List<int[]>();
                for (int seg = 0; seg < 4; seg++)
                {
                    long r = 0, g = 0, b = 0, n = 0;
                    for (int x = 0; x < 64; x++)
                        for (int y = 12 + seg * 6; y < 18 + seg * 6; y++)
                        {
                            Color c = small.GetPixel(x, y);
                            r += c.R; g += c.G; b += c.B; n++;
                        }
                    result.Add(Deepen((int)(r / n), (int)(g / n), (int)(b / n)));
                }
                palette = result.ToArray();
            }
        }
        catch { }
    }

    // Darkens and saturates a colour so it reads as a rich glass tint behind white icons.
    static int[] Deepen(int r, int g, int b)
    {
        double avg = (r + g + b) / 3.0;
        Func<int, int> f = v => Math.Max(0, Math.Min(255, (int)((avg + (v - avg) * 1.6) * 0.5)));
        return new[] { f(r), f(g), f(b) };
    }

    static bool SystemAnimationsOn()
    {
        bool on = true;
        SystemParametersInfo(0x1042, 0, ref on, 0);
        return on;
    }

    static bool BatterySaver()
    {
        PowerStatus s;
        return GetSystemPowerStatus(out s) && s.SaverFlag == 1;
    }

    // Fullscreen games (D3D) and presentation mode: taskbar is hidden, so do no work.
    // State 2 ("busy") is skipped: any window sized to the whole screen triggers it while the taskbar is still visible.
    static bool FullscreenAppRunning()
    {
        int state;
        return SHQueryUserNotificationState(out state) == 0 && (state == 3 || state == 4);
    }

    // ---------- animation ----------

    static double Approach(double cur, double target, double speed, double dt)
    {
        return cur + (target - cur) * (1 - Math.Exp(-speed * dt));
    }

    // Damped spring, sub-stepped for stability. Low damping = rubbery overshoot and wobble.
    static void Spring(ref double x, ref double v, double target, double k, double c, double dt)
    {
        const int steps = 4;
        double h = dt / steps;
        for (int i = 0; i < steps; i++)
        {
            double a = -k * (x - target) - c * v;
            v += a * h;
            x += v * h;
        }
    }

    // 0..1 glass-strength wave, different rhythm per theme
    static double Breath(double t)
    {
        if (theme == Theme.StrawHat)
        {
            // Ocean swell: a long roll with a shorter wave riding on it
            double swell = 0.6 * Math.Sin(t * Math.PI * 2 / 5) + 0.4 * Math.Sin(t * Math.PI * 2 / 2.3 + 1);
            return 0.5 + 0.5 * swell;
        }
        if (theme == Theme.Gear5)
        {
            // Drum beat: two quick thumps then rest, like a heartbeat
            double ph = (t % 2.6) / 2.6;
            double a = (ph - 0.1) / 0.12, b = (ph - 0.42) / 0.12;
            return Math.Min(1, Math.Exp(-a * a) + 0.7 * Math.Exp(-b * b));
        }
        return 0.5 + 0.5 * Math.Sin(t * Math.PI * 2 / 6);
    }

    static double[] Tint(double seconds, bool motion)
    {
        int[][] palette = ActivePalette();
        int[] a, b;
        double f = 0;
        if (colorCycle && motion)
        {
            double p = (seconds / CycleSeconds) % 1.0 * palette.Length;
            int i = (int)p;
            f = p - i;
            f = f * f * (3 - 2 * f);
            a = palette[i]; b = palette[(i + 1) % palette.Length];
        }
        else { a = b = palette[0]; }

        var t = new double[3];
        for (int k = 0; k < 3; k++)
        {
            t[k] = a[k] + (b[k] - a[k]) * f;
            if (lightTheme) t[k] = t[k] + (255 - t[k]) * 0.78;
        }
        return t;
    }

    static void SetAccent(IntPtr hwnd, Accent state, uint color)
    {
        var policy = new AccentPolicy { State = (int)state, Flags = 2, Color = color };
        int size = Marshal.SizeOf(policy);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, ptr, false);
            var data = new CompositionData { Attribute = 19, Data = ptr, Size = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    static bool ForegroundMaximizedOn(IntPtr bar)
    {
        IntPtr fg = GetForegroundWindow();
        return fg != IntPtr.Zero && IsZoomed(fg) && MonitorFromWindow(fg, 2) == MonitorFromWindow(bar, 2);
    }

    // Apple-dock shape from a UI Automation scan of the taskbar's items:
    //   * one centred dock pill around Start, Search and every app button,
    //   * one small pill around the tray (Wi-Fi, volume, battery, clock, notifications),
    //   * the far-left widgets/weather button is left out (the desktop weather widget replaces it).
    // items receives each dock button's rectangle, used for the hover tile.
    // shapes receives the icons-only outline: one rounded square per app icon and one tight box per tray item.
    static List<Rectangle> FindGroups(IntPtr hwnd, RECT r, float scale, List<Rectangle> items, List<Rectangle> shapes)
    {
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        var spans = new List<int[]>();
        var buttons = new List<Rectangle>();
        var all = new List<Rectangle>();
        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            foreach (AutomationElement el in root.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                System.Windows.Rect b = el.Current.BoundingRectangle;
                if (b.IsEmpty || b.Width < 4 || b.Height < 4) continue;
                if (b.Width > w * 0.6 || b.Top < r.Top - 2 || b.Bottom > r.Bottom + 2) continue;
                int x0 = (int)(b.Left - r.Left), x1 = (int)(b.Right - r.Left);
                spans.Add(new[] { x0, x1 });
                all.Add(new Rectangle(x0, (int)(b.Top - r.Top), x1 - x0, (int)b.Height));
                if (b.Height >= h * 0.6 && b.Width >= h * 0.6 && b.Width <= h * 1.6)
                    buttons.Add(new Rectangle(x0, (int)(b.Top - r.Top), x1 - x0, (int)b.Height));
            }
        }
        catch { }

        var groups = new List<Rectangle>();
        if (spans.Count == 0) return groups;
        spans.Sort((a, b) => a[0].CompareTo(b[0]));

        // first cluster items that sit close together
        int gap = (int)(18 * scale);
        var clusters = new List<int[]>();
        int s0 = spans[0][0], s1 = spans[0][1];
        foreach (var sp in spans)
        {
            if (sp[0] <= s1 + gap) { s1 = Math.Max(s1, sp[1]); continue; }
            clusters.Add(new[] { s0, s1 });
            s0 = sp[0]; s1 = sp[1];
        }
        clusters.Add(new[] { s0, s1 });

        // then sort clusters into dock (centre), tray (right edge) and the dropped widgets button (left edge)
        int dock0 = int.MaxValue, dock1 = int.MinValue, tray0 = int.MaxValue, tray1 = int.MinValue;
        foreach (var c in clusters)
        {
            double mid = (c[0] + c[1]) / 2.0 / w;
            if (mid < 0.15) continue;
            if (mid > 0.78) { tray0 = Math.Min(tray0, c[0]); tray1 = Math.Max(tray1, c[1]); }
            else { dock0 = Math.Min(dock0, c[0]); dock1 = Math.Max(dock1, c[1]); }
        }

        int pad = (int)(10 * scale), inset = (int)(5 * scale), edge = (int)(6 * scale);
        int top = inset, height = h - 2 * inset;
        if (dock1 > dock0)
        {
            var dock = new Rectangle(Math.Max(edge, dock0 - pad), top, 0, height);
            dock.Width = Math.Min(w - edge, dock1 + pad) - dock.X;
            groups.Add(dock);
            int icon = (int)(h * 0.62);
            foreach (var bt in buttons)
                if (bt.Left >= dock.Left && bt.Right <= dock.Right)
                {
                    items.Add(new Rectangle(bt.X + (int)(2 * scale), top + (int)(3 * scale), bt.Width - (int)(4 * scale), height - (int)(6 * scale)));
                    shapes.Add(new Rectangle(bt.X + (bt.Width - icon) / 2, bt.Y + (bt.Height - icon) / 2, icon, icon));
                }
        }
        if (tray1 > tray0)
        {
            var tray = new Rectangle(Math.Max(edge, tray0 - pad), top, 0, height);
            tray.Width = Math.Min(w - edge, tray1 + pad) - tray.X;
            groups.Add(tray);
            // tray: keep only innermost items (a container box would bring the bar background back)
            var inTray = all.FindAll(a => a.Left >= tray0 - 2 && a.Right <= tray1 + 2);
            foreach (var a in inTray)
            {
                bool container = inTray.Exists(o => o != a && a.Contains(o) && o.Width * o.Height < a.Width * a.Height);
                if (!container) { var t = a; t.Inflate(-1, -1); shapes.Add(t); }
            }
        }
        return groups;
    }

    [DllImport("gdi32.dll")] static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int l, int t, int r, int b);

    static void ApplyRegion(IntPtr hwnd, List<Rectangle> groups, List<Rectangle> shapes)
    {
        if (!floating || groups == null) { SetWindowRgn(hwnd, IntPtr.Zero, true); return; }
        IntPtr rgn = CreateRoundRectRgn(0, 0, 0, 0, 0, 0);
        // Icons only: the bar shows nothing but the icons themselves; everything between them is cut away.
        if (iconsOnly && shapes != null && shapes.Count > 0)
        {
            int round = (int)(10 * GetDpiForWindow(hwnd) / 96f);
            foreach (var sh in shapes)
            {
                IntPtr part = CreateRoundRectRgn(sh.Left, sh.Top, sh.Right + 1, sh.Bottom + 1, round, round);
                CombineRgn(rgn, rgn, part, 2);
                DeleteObject(part);
            }
            SetWindowRgn(hwnd, rgn, true);
            return;
        }
        foreach (var g in groups)
        {
            IntPtr part = CreateRoundRectRgn(g.Left, g.Top, g.Right + 1, g.Bottom + 1, g.Height, g.Height);
            CombineRgn(rgn, rgn, part, 2);
            DeleteObject(part);
        }
        SetWindowRgn(hwnd, rgn, true);
    }

    // Pop-in when the bar appears: each icon (or pill) grows from its centre with a springy overshoot,
    // staggered left to right, so the bar "pops" up like a dock coming alive.
    const double PopEach = 0.26, PopStagger = 0.012;

    static double PopDuration(int count) { return PopEach + PopStagger * Math.Max(0, count - 1); }

    static double EaseOutBack(double x)
    {
        x = Math.Max(0, Math.Min(1, x));
        const double c1 = 1.9, c3 = c1 + 1;
        return 1 + c3 * Math.Pow(x - 1, 3) + c1 * Math.Pow(x - 1, 2);
    }

    static void ApplyPopRegion(IntPtr hwnd, List<Rectangle> parts, double t, bool icons, float scale)
    {
        var order = new List<Rectangle>(parts);
        order.Sort((a, b) => a.X.CompareTo(b.X));
        IntPtr rgn = CreateRoundRectRgn(0, 0, 0, 0, 0, 0);
        int round = (int)(10 * scale);
        for (int i = 0; i < order.Count; i++)
        {
            double k = EaseOutBack((t - i * PopStagger) / PopEach);
            if (k <= 0.02) continue;
            var p = order[i];
            double cw = p.Width * k, ch = p.Height * k;
            int x0 = (int)(p.X + (p.Width - cw) / 2), y0 = (int)(p.Y + (p.Height - ch) / 2);
            int x1 = (int)(x0 + cw), y1 = (int)(y0 + ch);
            int rr = icons ? (int)(round * Math.Min(1, k)) : (int)ch;
            IntPtr part = CreateRoundRectRgn(x0, y0, x1 + 1, y1 + 1, Math.Max(2, rr), Math.Max(2, rr));
            CombineRgn(rgn, rgn, part, 2);
            DeleteObject(part);
        }
        SetWindowRgn(hwnd, rgn, true);
    }

    // Mac-style dock: auto-hide on (it slides away as soon as the mouse leaves). Explorer restarts
    // have flipped this setting before, so it is re-checked every few seconds.
    static void EnsureAutoHide(IntPtr hwnd, bool on)
    {
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)), hWnd = hwnd };
        bool isOn = (SHAppBarMessage(4, ref abd).ToInt64() & 1) != 0;
        if (isOn == on) return;
        abd.lParam = new IntPtr(on ? 1 : 2);
        SHAppBarMessage(10, ref abd);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint cb; public uint edge; public int l, t, r, b; public IntPtr lParam; }
    [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

    // Called ~30 times a second, but only repaints when something visible changed
    // (bar moved or appeared, the dock's items changed, or the hovered icon changed).
    static bool Animate(IntPtr hwnd, double dt, double seconds, bool motion, Accent glass)
    {
        RECT r;
        if (!GetWindowRect(hwnd, out r)) return false;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        bool main = hwnd == FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);

        Bar s;
        if (!bars.TryGetValue(hwnd, out s)) { s = new Bar(); bars[hwnd] = s; }
        if (main && seconds - s.LastAutoHideCheck > 3)
        {
            EnsureAutoHide(hwnd, autoHide);
            s.LastAutoHideCheck = seconds;
        }
        float scale = GetDpiForWindow(hwnd) / 96f;
        bool changed = false;
        Rectangle screen = Screen.FromHandle(hwnd).Bounds;
        bool offscreen = r.Top >= screen.Bottom - (int)(4 * scale);

        // The UI Automation scan takes up to ~1 s, so it runs on a background thread and its result is
        // picked up here on a later tick. While auto-hidden Windows reports no icon positions, so it only
        // rescans while the bar is visible and otherwise keeps the last good shape.
        if (s.Groups == null) s.Groups = new List<Rectangle> { new Rectangle(0, 0, w, h) };
        if (!s.Scanning && seconds - s.LastScan > 4 && !offscreen)
        {
            s.LastScan = seconds;
            s.Scanning = true;
            RECT scanRect = r;
            var bar = s;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var it = new List<Rectangle>();
                var sh = new List<Rectangle>();
                List<Rectangle> gr;
                try { gr = floating ? FindGroups(hwnd, scanRect, scale, it, sh) : new List<Rectangle>(); }
                catch { gr = new List<Rectangle>(); }
                bar.Pending = new object[] { gr, sh, it };
                bar.Scanning = false;
            });
        }
        object[] result = s.Pending;
        if (result != null)
        {
            s.Pending = null;
            var groups = (List<Rectangle>)result[0];
            var shapes = (List<Rectangle>)result[1];
            var items = (List<Rectangle>)result[2];
            bool empty = groups.Count == 0;
            if (empty) groups.Add(new Rectangle(0, 0, w, h));
            if (empty && floating && s.Groups != null && s.Shapes != null && s.Shapes.Count > 0) { groups = s.Groups; shapes = s.Shapes; items = s.Items; }
            string key = w + "x" + h + ":" + iconsOnly + ":" + string.Join(";", groups.ConvertAll(g => g.X + "," + g.Width).ToArray())
                       + "|" + string.Join(";", shapes.ConvertAll(g => g.X + "," + g.Y + "," + g.Width + "," + g.Height).ToArray());
            s.Items = items;
            if (key != s.GroupKey)
            {
                s.GroupKey = key;
                s.Groups = groups;
                s.Shapes = shapes;
                changed = true;
                if (!s.HiddenRegion) ApplyRegion(hwnd, groups, shapes);
            }
        }

        if (seconds - s.LastAccent > 1)
        {
            SetAccent(hwnd, Accent.Transparent, 0);
            s.LastAccent = seconds;
        }

        // Plain mode: no overlay, no shape, just Windows' own see-through taskbar.
        if (!floating)
        {
            if (s.Layer != null && !s.Layer.IsDisposed && s.Layer.Visible) s.Layer.Hide();
            return false;
        }

        // When auto-hide slides the bar off-screen, hide the glass too so nothing is left floating.
        if (s.Layer == null || s.Layer.IsDisposed) s.Layer = new Overlay();
        if (offscreen)
        {
            if (s.Layer.Visible) s.Layer.Hide();
            // While hidden, only a 2 px strip of the bar is on screen and Windows watches it to bring the
            // bar back, so the dock shape must not cut it away: keep a thin full-width strip instead.
            if (!s.HiddenRegion)
            {
                SetWindowRgn(hwnd, CreateRectRgn(0, 0, w, (int)(4 * scale)), true);
                s.HiddenRegion = true;
            }
            s.LastTop = r.Top;
            s.PopStart = -1;
            return false;
        }
        if (s.HiddenRegion)
        {
            s.HiddenRegion = false;
            s.PopStart = seconds; // the bar just appeared: pop the icons in
            changed = true;
        }
        if (s.PopStart >= 0)
        {
            double pt = seconds - s.PopStart;
            var parts = iconsOnly && s.Shapes != null && s.Shapes.Count > 0 ? s.Shapes : s.Groups;
            if (pt < PopDuration(parts.Count))
            {
                ApplyPopRegion(hwnd, parts, pt, iconsOnly, scale);
                if (!iconsOnly && changed) s.Layer.Render(r, s.Groups, Rectangle.Empty, true);
                return true; // keep 60 fps until the pop finishes
            }
            s.PopStart = -1;
            ApplyRegion(hwnd, s.Groups, s.Shapes);
        }
        // Icons only: no glass at all.
        if (iconsOnly)
        {
            if (s.Layer.Visible) s.Layer.Hide();
            return false;
        }
        if (!s.Layer.Visible) { s.Layer.Show(); changed = true; }
        if (r.Top != s.LastTop || r.Left != s.LastLeft) { s.LastTop = r.Top; s.LastLeft = r.Left; changed = true; }

        // hover tile under the dock icon the mouse is on
        POINT c;
        GetCursorPos(out c);
        Rectangle hover = Rectangle.Empty;
        if (s.Items != null)
            foreach (var it in s.Items)
                if (c.X >= r.Left + it.Left && c.X < r.Left + it.Right && c.Y >= r.Top && c.Y < r.Bottom) { hover = it; break; }
        if (hover != s.Hovered) { s.Hovered = hover; changed = true; }

        if (changed) s.Layer.Render(r, s.Groups, s.Hovered, true);
        return !s.Hovered.IsEmpty;
    }

    static int Clamp(double v) { return (int)Math.Max(0, Math.Min(255, v)); }

    // ---------- colour overlay ----------
    // Windows 11 ignores the accent tint on the taskbar, so colour, waves and glow are painted on a
    // per-pixel-alpha, click-through window that sits exactly on top of the taskbar.

    static readonly Color[] StrawHatBright =
    {
        Color.FromArgb(245, 190, 40), Color.FromArgb(225, 35, 45), Color.FromArgb(35, 120, 230), Color.FromArgb(250, 125, 35),
    };
    static readonly Color[] Gear5Bright =
    {
        Color.FromArgb(205, 185, 255), Color.FromArgb(255, 255, 255), Color.FromArgb(165, 135, 245), Color.FromArgb(235, 225, 255),
    };

    static Color[] BrightPalette()
    {
        if (theme == Theme.StrawHat) return StrawHatBright;
        if (theme == Theme.Gear5) return Gear5Bright;
        var list = new Color[palette.Length];
        for (int i = 0; i < palette.Length; i++)
            list[i] = Color.FromArgb(Clamp(palette[i][0] * 2.2), Clamp(palette[i][1] * 2.2), Clamp(palette[i][2] * 2.2));
        return list;
    }

    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLEND { public byte Op, Flags, Alpha, Format; }

    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLEND blend, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    class Overlay : Form
    {
        // Drawing goes straight into a premultiplied DIB that stays selected in a memory DC,
        // so presenting a frame is one UpdateLayeredWindow call with no copies or conversions.
        Bitmap frame;
        int lastTopmost = -100000;
        IntPtr memDC, dib, oldObj;

        void EnsureSurface(int w, int h)
        {
            if (frame != null && frame.Width == w && frame.Height == h) return;
            FreeSurface();
            IntPtr screen = GetDC(IntPtr.Zero);
            memDC = CreateCompatibleDC(screen);
            ReleaseDC(IntPtr.Zero, screen);
            var bmi = new BITMAPINFOHEADER { Size = Marshal.SizeOf(typeof(BITMAPINFOHEADER)), Width = w, Height = -h, Planes = 1, BitCount = 32 };
            IntPtr bits;
            dib = CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
            oldObj = SelectObject(memDC, dib);
            frame = new Bitmap(w, h, w * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
        }

        void FreeSurface()
        {
            if (frame != null) { frame.Dispose(); frame = null; }
            if (memDC != IntPtr.Zero) { SelectObject(memDC, oldObj); DeleteObject(dib); DeleteDC(memDC); memDC = IntPtr.Zero; }
        }

        protected override void Dispose(bool disposing) { FreeSurface(); base.Dispose(disposing); }

        public Overlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // layered | click-through | tool window | no-activate | topmost
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000 | 0x8;
                return cp;
            }
        }

        static GraphicsPath Pill(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height)));
            if (d <= 2) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static Color A(Color c, double alpha) { return Color.FromArgb(Clamp(alpha), c.R, c.G, c.B); }

        // Frosted light glass, Apple-dock style: soft white fill with a gentle sheen, a light hairline,
        // a soft shadow underneath, and a light tile under the hovered icon. No glow, no colour wash.
        public void Render(RECT bar, List<Rectangle> pills, Rectangle hoverTile, bool rounded)
        {
            int w = bar.Right - bar.Left, h = bar.Bottom - bar.Top;
            if (w <= 0 || h <= 0) return;
            EnsureSurface(w, h);
            float scale = h / 48f;

            using (var g = Graphics.FromImage(frame))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighSpeed;

                foreach (var p in pills)
                {
                    var area = new RectangleF(p.X, p.Y, p.Width, p.Height);
                    float radius = rounded ? p.Height : 1;

                    // soft shadow just below the pill
                    for (int i = 3; i >= 1; i--)
                    {
                        var sh = new RectangleF(area.X - i, area.Y + i * 1.2f * scale, area.Width + 2 * i, area.Height + i);
                        using (var sp = Pill(sh, radius + 2 * i))
                        using (var sb = new SolidBrush(Color.FromArgb(14, 0, 0, 0)))
                            g.FillPath(sb, sp);
                    }

                    using (var path = Pill(area, radius))
                    {
                        using (var fill = new LinearGradientBrush(area, Color.FromArgb(62, 255, 255, 255), Color.FromArgb(34, 255, 255, 255), 90f))
                            g.FillPath(fill, path);

                        // top sheen: a thin brighter band near the upper edge
                        var sheen = new RectangleF(area.X + radius * 0.25f, area.Y + 1, area.Width - radius * 0.5f, area.Height * 0.42f);
                        if (sheen.Width > 4)
                            using (var sp = Pill(sheen, sheen.Height))
                            using (var sb = new LinearGradientBrush(sheen, Color.FromArgb(34, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                                g.FillPath(sb, sp);

                        using (var edge = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
                            g.DrawPath(edge, path);
                    }
                }

                if (!hoverTile.IsEmpty)
                    using (var tp = Pill(new RectangleF(hoverTile.X, hoverTile.Y, hoverTile.Width, hoverTile.Height), 10 * scale))
                    using (var tb = new SolidBrush(Color.FromArgb(36, 255, 255, 255)))
                        g.FillPath(tb, tp);
            }

            Present(bar.Left, bar.Top, w, h);
        }

        void Present(int x, int y, int w, int h)
        {
            var size = new SIZE { cx = w, cy = h };
            var src = new POINT();
            var pos = new POINT { X = x, Y = y };
            var blend = new BLEND { Op = 0, Flags = 0, Alpha = 255, Format = 1 };
            UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref size, memDC, ref src, 0, ref blend, 2);

            // Stay above the taskbar without taking focus.
            // Stay above the taskbar without taking focus; once a second is enough.
            if (Environment.TickCount - lastTopmost > 1000)
            {
                SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040);
                lastTopmost = Environment.TickCount;
            }
        }
    }

    static List<IntPtr> Taskbars()
    {
        var list = new List<IntPtr>();
        IntPtr main = FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null);
        if (main != IntPtr.Zero) list.Add(main);
        IntPtr sec = IntPtr.Zero;
        while ((sec = FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(sec);
        return list;
    }

    static void Tick()
    {
        double now = clock.Elapsed.TotalSeconds;
        double dt = Math.Min(0.1, now - lastTick);
        lastTick = now;

        if (FullscreenAppRunning())
        {
            foreach (var b in bars.Values) if (b.Layer != null && b.Layer.Visible) b.Layer.Hide();
            timer.Interval = 500;
            return;
        }

        bool saver = BatterySaver();
        bool motion = animate && (!followWindows || SystemAnimationsOn()) && !saver;
        Accent glass = saver && mode == Accent.Acrylic ? Accent.BlurBehind : mode;

        // Attention pulse: two soft breaths, then fade
        double since = now - pulseStart;
        pulse = since < 2.4 ? Math.Pow(Math.Sin(since / 1.2 * Math.PI), 2) * (since < 1.2 ? 1 : 0.6) : 0;

        // Explorer restarts create new taskbar windows; drop the stale ones.
        foreach (IntPtr dead in new List<IntPtr>(bars.Keys))
            if (!IsWindow(dead))
            {
                if (bars[dead].Layer != null) bars[dead].Layer.Close();
                bars.Remove(dead);
            }

        bool moving = pulse > 0;
        foreach (IntPtr bar in Taskbars()) moving |= Animate(bar, dt, now, motion, glass);

        if (debugLog != null)
            foreach (var kv in bars)
                debugLog.WriteLine("{0:F3} interval={1} hiddenRgn={2} pop={3:F2} groups={4} shapes={5} items={6}", now, timer.Interval, kv.Value.HiddenRegion, kv.Value.PopStart, kv.Value.Groups == null ? -1 : kv.Value.Groups.Count, kv.Value.Shapes == null ? -1 : kv.Value.Shapes.Count, kv.Value.Items == null ? -1 : kv.Value.Items.Count);

        // Springs need 60 fps; the flowing colour layer looks smooth at 30 fps.
        // 60 fps only while something moves (hover, springs, sweep); the slow colour drift needs just 5 fps.
        // 60 fps while the mouse moves over dock icons; otherwise ~30 checks a second so the glass follows the bar the instant it appears.
        timer.Interval = moving ? 16 : (floating ? 33 : 500);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern void SHChangeNotify(int e, uint f, string a, IntPtr b);

    // Desktop folder icons: OneDrive/Explorer sometimes rewrite desktop.ini and drop the custom icon.
    // icons.txt next to the icons lists "Folder name|icon.ico"; any folder that lost its icon gets it back.
    static void HealFolderIcons()
    {
        try
        {
            string iconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), @"Wallpapers\FolderIcons");
            string list = Path.Combine(iconDir, "icons.txt");
            if (!File.Exists(list)) return;
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            foreach (string line in File.ReadAllLines(list))
            {
                string[] parts = line.Split('|');
                if (parts.Length != 2) continue;
                string dir = Path.Combine(desk, parts[0].Trim());
                if (!Directory.Exists(dir)) continue;
                string ini = Path.Combine(dir, "desktop.ini");
                string icon = Path.Combine(iconDir, parts[1].Trim());
                if (File.Exists(ini) && File.ReadAllText(ini).Contains(icon)) continue;

                if (File.Exists(ini)) File.SetAttributes(ini, FileAttributes.Normal);
                File.WriteAllText(ini, "[.ShellClassInfo]\r\nIconResource=" + icon + ",0\r\n[ViewState]\r\nMode=\r\nVid=\r\nFolderType=Generic\r\n", System.Text.Encoding.Unicode);
                File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
                var di = new DirectoryInfo(dir);
                di.Attributes |= FileAttributes.ReadOnly;
                di.LastWriteTime = DateTime.Now;
                SHChangeNotify(0x00002000, 0x0005, dir, IntPtr.Zero);
            }
        }
        catch { }
    }

    static void Restore()
    {
        foreach (var b in bars.Values) if (b.Layer != null && !b.Layer.IsDisposed) b.Layer.Hide();
        foreach (IntPtr bar in Taskbars())
        {
            SetAccent(bar, Accent.Disabled, 0);
            SetWindowRgn(bar, IntPtr.Zero, true);
        }
    }

    // Hidden window that hears "app is flashing for attention" from the shell.
    class ShellListener : NativeWindow
    {
        readonly int shellMsg;
        public ShellListener()
        {
            CreateHandle(new CreateParams());
            shellMsg = RegisterWindowMessage("SHELLHOOK");
            RegisterShellHookWindow(Handle);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == shellMsg && ((int)m.WParam & 0x7FFF) == 6 && clock.Elapsed.TotalSeconds - pulseStart > 2.4)
                pulseStart = clock.Elapsed.TotalSeconds;
            base.WndProc(ref m);
        }
    }

    static ToolStripMenuItem Toggle(string text, bool initial, Action<bool> set)
    {
        var item = new ToolStripMenuItem(text) { Checked = initial, CheckOnClick = true };
        item.CheckedChanged += (s, e) => { set(item.Checked); SaveSettings(); };
        return item;
    }

    [STAThread]
    static void Main()
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));
        bool fresh;
        using (new Mutex(true, "TaskbarGlass.Single", out fresh))
        {
            if (!fresh) return;

            if (Environment.GetCommandLineArgs().Length > 1 && Environment.GetCommandLineArgs()[1] == "--debug")
                debugLog = new StreamWriter(Path.Combine(Path.GetTempPath(), "TaskbarGlass-debug.log")) { AutoFlush = true };
            LoadSettings();
            ReadTheme();
            ReadWallpaperPalette();
            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (e.Category == UserPreferenceCategory.Desktop) ReadWallpaperPalette();
                if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.VisualStyle) ReadTheme();
            };
            SystemEvents.SessionEnding += (s, e) => Restore();
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Restore();
            Application.ThreadException += (s, e) => Restore();

            var listener = new ShellListener();

            var menu = new ContextMenuStrip();
            var glass = new ToolStripMenuItem("Frosted glass") { Checked = mode == Accent.Acrylic };
            var clear = new ToolStripMenuItem("Clear glass") { Checked = mode == Accent.BlurBehind };
            glass.Click += (s, e) => { mode = Accent.Acrylic; glass.Checked = true; clear.Checked = false; SaveSettings(); };
            clear.Click += (s, e) => { mode = Accent.BlurBehind; clear.Checked = true; glass.Checked = false; SaveSettings(); };
            var anim = Toggle("Animations", animate, v => animate = v);

            var themes = new ToolStripMenuItem("Theme");
            var themeItems = new List<ToolStripMenuItem>();
            foreach (var pair in new[]
            {
                new KeyValuePair<Theme, string>(Theme.StrawHat, "Straw Hat (Luffy)"),
                new KeyValuePair<Theme, string>(Theme.Gear5, "Gear 5 (Luffy)"),
                new KeyValuePair<Theme, string>(Theme.Wallpaper, "Match wallpaper"),
            })
            {
                var value = pair.Key;
                var item = new ToolStripMenuItem(pair.Value) { Checked = theme == value };
                item.Click += (s, e) =>
                {
                    theme = value;
                    foreach (var other in themeItems) other.Checked = other == item;
                    SaveSettings();
                };
                themeItems.Add(item);
                themes.DropDownItems.Add(item);
            }
            menu.Items.Add(themes);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(glass);
            menu.Items.Add(clear);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Toggle("Apple-style dock (off = plain bar)", floating, v => floating = v));
            menu.Items.Add(Toggle("Icons only (no bar behind them)", iconsOnly, v => { iconsOnly = v; foreach (var b in bars.Values) b.GroupKey = null; }));
            menu.Items.Add(Toggle("Auto-hide dock", autoHide, v => { autoHide = v; foreach (var b in bars.Values) b.LastAutoHideCheck = -10; }));
            menu.Items.Add(anim);
            menu.Items.Add(Toggle("Colour drift", colorCycle, v => colorCycle = v));
            menu.Items.Add(Toggle("Glow when an app needs attention", alerts, v => alerts = v));
            menu.Items.Add(Toggle("Pause when Windows animations are off", followWindows, v => followWindows = v));
            menu.Items.Add(new ToolStripSeparator());
            var startup = new ToolStripMenuItem("Start with Windows") { Checked = File.Exists(StartupLink), CheckOnClick = true };
            startup.CheckedChanged += (s, e) => SetStartup(startup.Checked);
            menu.Items.Add(startup);
            menu.Items.Add("Exit (restore normal taskbar)", null, (s, e) => { Restore(); Application.Exit(); });

            var tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Taskbar Glass — double-click to toggle animations", ContextMenuStrip = menu, Visible = true };
            string ico = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "glass.ico");
            if (File.Exists(ico)) tray.Icon = new Icon(ico);
            tray.DoubleClick += (s, e) => anim.Checked = !anim.Checked;

            timer = new System.Windows.Forms.Timer { Interval = 16 };
            timer.Tick += (s, e) => Tick();
            timer.Start();

            var iconTimer = new System.Windows.Forms.Timer { Interval = 60000 };
            iconTimer.Tick += (s, e) => HealFolderIcons();
            iconTimer.Start();
            HealFolderIcons();

            Application.ApplicationExit += (s, e) => { tray.Visible = false; listener.DestroyHandle(); };
            Application.Run();
        }
    }
}
