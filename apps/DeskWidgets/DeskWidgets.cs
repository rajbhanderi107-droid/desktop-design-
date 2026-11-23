using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

// Desktop widgets in the Straw Hat theme. Every widget is a per-pixel-alpha window that sits behind
// normal windows, can be dragged (it snaps to an 8 px grid), lifts on hover and fades in on start.

static class Theme
{
    public static readonly Color CardTop = Color.FromArgb(242, 36, 42, 59);
    public static readonly Color CardBottom = Color.FromArgb(248, 14, 19, 31);
    public static readonly Color Gold = Color.FromArgb(247, 205, 126);
    public static readonly Color Red = Color.FromArgb(235, 102, 103);
    public static readonly Color Sea = Color.FromArgb(122, 178, 231);
    public static readonly Color Text = Color.FromArgb(246, 242, 236);
    public static readonly Color Dim = Color.FromArgb(200, 231, 234, 244);
    public const string Display = "Segoe UI Variable Display";
    public const string Body = "Segoe UI Variable Text";
    public const string Icons = "Segoe Fluent Icons";

    // Grid: small widget = 164, medium = 2 * 164 + gap, gap = 14 (all in logical pixels)
    public const int Unit = 164, Gap = 14;
    public const int Medium = Unit * 2 + Gap;
}

static class Settings
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskWidgets");
    public static readonly string File_ = Path.Combine(Dir, "settings.ini");
    public static readonly string TodoFile = Path.Combine(Dir, "todo.txt");
    public static readonly string PhotoDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Widgets");
    static readonly Dictionary<string, string> values = new Dictionary<string, string>();

    public static void Load(bool readOnly = false)
    {
        if (!readOnly)
        {
            Directory.CreateDirectory(Dir);
            Directory.CreateDirectory(PhotoDir);
            if (!File.Exists(File_))
                File.WriteAllText(File_, "# Weather location (change city name and coordinates, then right-click a widget > Reload)\r\ncity=Ahmedabad\r\nlat=23.0225\r\nlon=72.5714\r\n");
        }
        values.Clear();
        if (!File.Exists(File_)) return;
        foreach (string line in File.ReadAllLines(File_))
        {
            int eq = line.IndexOf('=');
            if (eq > 0 && !line.StartsWith("#")) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
    }

    public static string Get(string key, string fallback)
    {
        string v;
        return values.TryGetValue(key, out v) ? v : fallback;
    }

    public static void Set(string key, string value)
    {
        values[key] = value;
        var lines = new List<string>();
        bool found = false;
        foreach (string line in File.ReadAllLines(File_))
        {
            if (line.StartsWith(key + "=")) { lines.Add(key + "=" + value); found = true; }
            else lines.Add(line);
        }
        if (!found) lines.Add(key + "=" + value);
        File.WriteAllLines(File_, lines.ToArray());
    }

    public static void ClearPositions()
    {
        var lines = new List<string>();
        foreach (string line in File.ReadAllLines(File_)) if (!line.StartsWith("pos.")) lines.Add(line);
        File.WriteAllLines(File_, lines.ToArray());
        foreach (string k in new List<string>(values.Keys)) if (k.StartsWith("pos.")) values.Remove(k);
    }
}

static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLEND { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPels, YPels, ClrUsed, ClrImportant;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPOS { public IntPtr hwnd, hwndInsertAfter; public int x, y, cx, cy; public uint flags; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, int key, ref BLEND blend, int flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("user32.dll")] public static extern bool ReleaseCapture();
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    [DllImport("user32.dll")] public static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    public static void MediaKey(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, 2, UIntPtr.Zero);
    }
}

// Dark menu that matches the cards.
class DarkColors : ProfessionalColorTable
{
    static readonly Color Bg = Color.FromArgb(30, 24, 44), Hi = Color.FromArgb(70, 54, 96), Line = Color.FromArgb(60, 255, 255, 255);
    public override Color ToolStripDropDownBackground { get { return Bg; } }
    public override Color ImageMarginGradientBegin { get { return Bg; } }
    public override Color ImageMarginGradientMiddle { get { return Bg; } }
    public override Color ImageMarginGradientEnd { get { return Bg; } }
    public override Color MenuBorder { get { return Color.FromArgb(70, 60, 96); } }
    public override Color MenuItemBorder { get { return Hi; } }
    public override Color MenuItemSelected { get { return Hi; } }
    public override Color SeparatorDark { get { return Line; } }
    public override Color SeparatorLight { get { return Bg; } }
}

abstract class Widget : Form
{
    public readonly string Id;
    protected readonly float Sc;
    protected readonly int Pad;
    Bitmap frame;
    IntPtr memDC, dib, oldObj;
    byte alpha;
    double hover, hoverTarget;
    readonly System.Windows.Forms.Timer anim = new System.Windows.Forms.Timer { Interval = 16 };
    readonly List<System.Windows.Forms.Timer> timers = new List<System.Windows.Forms.Timer>();
    public static ContextMenuStrip SharedMenu;
    public static bool SuppressMenu; // set by a widget that used a right-click itself
    public static bool Preview;
    public static float LayoutScale = 1f;

    protected Widget(string id, int w, int h, int x, int y)
    {
        Id = id;
        Sc = LayoutScale;
        Pad = (int)(16 * Sc);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size((int)(w * Sc) + 2 * Pad, (int)(h * Sc) + 2 * Pad);
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        Point p = new Point(area.Left + (int)(x * Sc) - Pad, area.Top + (int)(y * Sc) - Pad);
        string saved = Settings.Get("pos." + id, null);
        if (saved != null)
        {
            string[] xy = saved.Split(',');
            int sx, sy;
            if (xy.Length == 2 && int.TryParse(xy[0], out sx) && int.TryParse(xy[1], out sy))
            {
                var card = new Rectangle(sx + Pad, sy + Pad, Width - 2 * Pad, Height - 2 * Pad);
                foreach (Screen screen in Screen.AllScreens)
                    if (screen.WorkingArea.Contains(card)) { p = new Point(sx, sy); break; }
            }
        }
        Location = p;
        ContextMenuStrip = SharedMenu;
        anim.Tick += (s, e) => Step();
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x80 | 0x8000000; // layered | tool window | no-activate
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x46) // keep at the bottom of the window stack, just above the desktop
        {
            var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
            wp.hwndInsertAfter = new IntPtr(1);
            wp.flags &= ~0x4u;
            Marshal.StructureToPtr(wp, m.LParam, false);
        }
        base.WndProc(ref m);
        if (m.Msg == 0x232) SnapAndSave();
    }

    // After a drag: snap the card to an 8 px grid and keep it fully on screen.
    void SnapAndSave()
    {
        int grid = (int)(8 * Sc);
        Rectangle wa = Screen.FromControl(this).WorkingArea;
        int cx = (int)Math.Round((Left + Pad) / (double)grid) * grid, cy = (int)Math.Round((Top + Pad) / (double)grid) * grid;
        cx = Math.Max(wa.Left + grid, Math.Min(wa.Right - (Width - 2 * Pad) - grid, cx));
        cy = Math.Max(wa.Top + grid, Math.Min(wa.Bottom - (Height - 2 * Pad) - grid, cy));
        Location = new Point(cx - Pad, cy - Pad);
        Settings.Set("pos." + Id, Left + "," + Top);
        Redraw();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        alpha = 0;
        anim.Start();
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hoverTarget = 1; anim.Start(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverTarget = 0; anim.Start(); }

    void Step()
    {
        bool busy = false;
        if (alpha < 255) { alpha = (byte)Math.Min(255, alpha + 24); busy = true; }
        double d = hoverTarget - hover;
        if (Math.Abs(d) > 0.01) { hover += d * 0.3; busy = true; } else hover = hoverTarget;
        Redraw();
        if (!busy) anim.Stop();
    }

    // The card lifts 2 px on hover.
    protected RectangleF CardRect { get { return new RectangleF(Pad, Pad - (float)(hover * 2 * Sc), Width - 2 * Pad, Height - 2 * Pad); } }

    protected float S(float v) { return v * Sc; }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    protected Font F(float size, FontStyle style) { return new Font(Theme.Body, size * Sc, style, GraphicsUnit.Pixel); }
    protected Font D(float size, FontStyle style) { return new Font(Theme.Display, size * Sc, style, GraphicsUnit.Pixel); }
    protected float Radius { get { return S(18); } }

    // Fine film grain, tiled over every card: gives the flat gradients a printed, premium feel.
    static TextureBrush grain;
    static TextureBrush Grain
    {
        get
        {
            if (grain != null) return grain;
            var rnd = new Random(11);
            var bmp = new Bitmap(96, 96, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < 96; y++)
                for (int x = 0; x < 96; x++)
                {
                    int v = rnd.Next(256);
                    bmp.SetPixel(x, y, v > 128 ? Color.FromArgb((v - 128) / 12, 255, 255, 255) : Color.FromArgb((128 - v) / 10, 0, 0, 0));
                }
            grain = new TextureBrush(bmp, WrapMode.Tile);
            return grain;
        }
    }

    // Champagne gold for edges and headings.
    public static readonly Color ChampagneHi = Color.FromArgb(255, 248, 222, 160), ChampagneLo = Color.FromArgb(255, 196, 146, 58);

    // Cache the gradient, grain and rim by size and colour.
    readonly Dictionary<string, Bitmap> cardCache = new Dictionary<string, Bitmap>();
    Bitmap shadowCache;

    protected void FillCard(Graphics g, RectangleF card, Color top, Color bottom)
    {
        string key = (int)card.Width + "x" + (int)card.Height + ":" + top.ToArgb() + ":" + bottom.ToArgb();
        Bitmap bmp;
        if (!cardCache.TryGetValue(key, out bmp))
        {
            if (cardCache.Count > 8) { foreach (var b in cardCache.Values) b.Dispose(); cardCache.Clear(); }
            bmp = new Bitmap((int)Math.Ceiling(card.Width) + 2, (int)Math.Ceiling(card.Height) + 2, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var cg = Graphics.FromImage(bmp))
            {
                cg.SmoothingMode = SmoothingMode.AntiAlias;
                cg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                PaintCard(cg, new RectangleF(1, 1, card.Width, card.Height), top, bottom);
            }
            cardCache[key] = bmp;
        }
        g.DrawImageUnscaled(bmp, (int)Math.Round(card.X) - 1, (int)Math.Round(card.Y) - 1);
    }

    void PaintCard(Graphics g, RectangleF card, Color top, Color bottom)
    {
        using (var path = Round(card, Radius))
        {
            using (var b = new LinearGradientBrush(card, top, bottom, 90f))
            {
                var mid = Color.FromArgb((top.A + bottom.A) / 2, (top.R * 2 + bottom.R * 3) / 5, (top.G * 2 + bottom.G * 3) / 5, (top.B * 2 + bottom.B * 3) / 5);
                b.InterpolationColors = new ColorBlend(3) { Colors = new[] { top, mid, bottom }, Positions = new[] { 0f, 0.55f, 1f } };
                g.FillPath(b, path);
            }

            var state = g.Save();
            g.SetClip(path);
            using (var glowPath = new GraphicsPath())
            {
                var gr = new RectangleF(card.X - card.Width * 0.35f, card.Y - card.Height * 0.6f, card.Width * 1.1f, card.Height * 1.3f);
                glowPath.AddEllipse(gr);
                using (var glow = new PathGradientBrush(glowPath) { CenterColor = Color.FromArgb(17, 255, 196, 110), SurroundColors = new[] { Color.FromArgb(0, 255, 196, 110) } })
                    g.FillPath(glow, glowPath);
            }
            g.FillPath(Grain, path);
            g.Restore(state);

            var inner = card;
            inner.Inflate(-0.75f, -0.75f);
            using (var ip = Round(inner, Radius - 0.75f))
            using (var rim = new LinearGradientBrush(card, Color.FromArgb(78, ChampagneHi), Color.FromArgb(25, Theme.Sea), 90f))
            using (var pen = new Pen(rim, Math.Max(1f, S(0.8f))))
                g.DrawPath(pen, ip);
        }
    }

    protected void FillCard(Graphics g, RectangleF card) { FillCard(g, card, Theme.CardTop, Theme.CardBottom); }

    protected abstract void Draw(Graphics g, RectangleF card);

    // Return true when the click was used by the widget (otherwise it starts a drag).
    protected virtual bool HandleClick(PointF p) { return false; }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (HandleClick(new PointF(e.X, e.Y))) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
    }

    void EnsureSurface()
    {
        if (frame != null && frame.Width == Width && frame.Height == Height) return;
        FreeSurface();
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        memDC = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new Native.BITMAPINFOHEADER { Size = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), Width = Width, Height = -Height, Planes = 1, BitCount = 32 };
        IntPtr bits;
        dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
        oldObj = Native.SelectObject(memDC, dib);
        frame = new Bitmap(Width, Height, Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
    }

    void FreeSurface()
    {
        if (frame != null) { frame.Dispose(); frame = null; }
        if (memDC != IntPtr.Zero) { Native.SelectObject(memDC, oldObj); Native.DeleteObject(dib); Native.DeleteDC(memDC); memDC = IntPtr.Zero; }
    }

    protected void Repeat(int interval, Action tick)
    {
        if (Preview) return;
        var timer = new System.Windows.Forms.Timer { Interval = interval };
        timer.Tick += (s, e) => tick();
        timers.Add(timer);
        timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var timer in timers) timer.Dispose();
            anim.Dispose();
            foreach (var art in cardCache.Values) art.Dispose();
            if (shadowCache != null) shadowCache.Dispose();
            FreeSurface();
        }
        base.Dispose(disposing);
    }

    public void Redraw()
    {
        if (!IsHandleCreated) return;
        PaintFrame();
        var size = new Native.SIZE { cx = Width, cy = Height };
        var src = new Native.POINT();
        var pos = new Native.POINT { X = Left, Y = Top };
        var blend = new Native.BLEND { Op = 0, Flags = 0, Alpha = alpha, Format = 1 };
        Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref size, memDC, ref src, 0, ref blend, 2);
    }

    void PaintFrame()
    {
        EnsureSurface();
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            RectangleF card = CardRect;
            // Soft layered shadow, deeper while hovered
            // Deep, soft, slightly warm shadow (more layers, lower opacity each), deeper while hovered.
            // Cached at rest; only redrawn live while the hover lift is animating.
            bool resting = hover == 0;
            if (resting && shadowCache != null && shadowCache.Width == Width && shadowCache.Height == Height)
                g.DrawImageUnscaled(shadowCache, 0, 0);
            else
            {
                Bitmap target = resting ? new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb) : null;
                Graphics sg = resting ? Graphics.FromImage(target) : g;
                sg.SmoothingMode = SmoothingMode.AntiAlias;
                int layers = 10;
                for (int i = layers; i >= 1; i--)
                {
                    float spread = i * Sc * (1.25f + (float)hover * 0.45f);
                    var r = new RectangleF(card.X - spread * 0.8f, card.Y - spread * 0.6f + S(7) + (float)hover * S(3), card.Width + spread * 1.6f, card.Height + spread * 1.6f);
                    using (var path = Round(r, Radius + spread))
                    using (var b = new SolidBrush(Color.FromArgb((int)(7 + hover * 2), 12, 4, 10)))
                        sg.FillPath(b, path);
                }
                if (resting)
                {
                    sg.Dispose();
                    if (shadowCache != null) shadowCache.Dispose();
                    shadowCache = target;
                    g.DrawImageUnscaled(shadowCache, 0, 0);
                }
            }
            Draw(g, card);
        }
    }

    protected static void DrawCover(Graphics g, Image img, RectangleF dst, float radius, float opacity)
    {
        using (var path = Round(dst, radius))
        {
            if (img == null)
            {
                using (var b = new LinearGradientBrush(dst, Color.FromArgb(255, 90, 40, 110), Color.FromArgb(255, 230, 110, 60), 45f))
                    g.FillPath(b, path);
                return;
            }
            float scale = Math.Max(dst.Width / img.Width, dst.Height / img.Height);
            float w = img.Width * scale, h = img.Height * scale;
            var state = g.Save();
            g.SetClip(path);
            if (opacity >= 0.999f) g.DrawImage(img, dst.X + (dst.Width - w) / 2, dst.Y + (dst.Height - h) / 2, w, h);
            else
                using (var ia = new System.Drawing.Imaging.ImageAttributes())
                {
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = opacity };
                    ia.SetColorMatrix(cm);
                    g.DrawImage(img, new Rectangle((int)(dst.X + (dst.Width - w) / 2), (int)(dst.Y + (dst.Height - h) / 2), (int)w, (int)h), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, ia);
                }
            g.Restore(state);
            // photos get the same champagne hairline as the cards
            using (var rim = new LinearGradientBrush(dst, Color.FromArgb(140, ChampagneHi), Color.FromArgb(30, ChampagneLo), 90f))
            using (var pen = new Pen(rim, 1.2f)) g.DrawPath(pen, path);
        }
    }

    protected static Image LoadPhoto(string path)
    {
        if (!File.Exists(path)) return null;
        try { using (var s = new MemoryStream(File.ReadAllBytes(path))) return new Bitmap(Image.FromStream(s)); }
        catch { return null; }
    }

    protected void Label(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h)
    {
        using (var b = new SolidBrush(c))
        using (var sf = new StringFormat { Alignment = h, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
            g.DrawString(s, f, b, r, sf);
    }

    public virtual void Reload() { Redraw(); }

    public Bitmap Snapshot() { PaintFrame(); return new Bitmap(frame); }
}

// ---------------------------------------------------------------- photos

class PhotoWidget : Widget
{
    readonly string file;
    readonly bool rotate;
    Image img, previous;
    double fade = 1;
    int index;
    readonly System.Windows.Forms.Timer fadeTimer = new System.Windows.Forms.Timer { Interval = 33 };

    // rotate = cycle through every photo in Pictures\Widgets every 20 minutes with a crossfade
    public PhotoWidget(string id, string file, bool rotate, int x, int y) : base(id, Theme.Unit, Theme.Unit, x, y)
    {
        this.file = file;
        this.rotate = rotate;
        img = LoadPhoto(Path.Combine(Settings.PhotoDir, file));
        fadeTimer.Tick += (s, e) => { fade = Math.Min(1, fade + 0.06); Redraw(); if (fade >= 1) { fadeTimer.Stop(); if (previous != null) previous.Dispose(); previous = null; } };
        if (rotate) Repeat(20 * 60 * 1000, Next);
    }

    void Next()
    {
        var all = new List<string>();
        foreach (string ext in new[] { "*.jpg", "*.jpeg", "*.png" }) all.AddRange(Directory.GetFiles(Settings.PhotoDir, ext));
        if (all.Count < 2) return;
        all.Sort();
        index = (index + 1) % all.Count;
        previous = img;
        img = LoadPhoto(all[index]);
        fade = 0;
        fadeTimer.Start();
    }

    protected override void Draw(Graphics g, RectangleF card)
    {
        if (previous != null) DrawCover(g, previous, card, Radius, 1);
        DrawCover(g, img, card, Radius, (float)fade);
    }

    public override void Reload()
    {
        if (img != null) img.Dispose();
        img = LoadPhoto(Path.Combine(Settings.PhotoDir, file));
        Redraw();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            fadeTimer.Dispose();
            if (img != null) img.Dispose();
            if (previous != null) previous.Dispose();
        }
        base.Dispose(disposing);
    }
}

class StripWidget : Widget
{
    readonly string[] files;
    Image[] imgs;
    public StripWidget(string id, string[] files, int x, int y) : base(id, Theme.Medium, Theme.Unit, x, y) { this.files = files; LoadAll(); }
    void ReleaseImages() { if (imgs != null) foreach (var img in imgs) if (img != null) img.Dispose(); }
    void LoadAll() { ReleaseImages(); imgs = new Image[files.Length]; for (int i = 0; i < files.Length; i++) imgs[i] = LoadPhoto(Path.Combine(Settings.PhotoDir, files[i])); }
    protected override void Draw(Graphics g, RectangleF card)
    {
        FillCard(g, card);
        float gap = S(10), inner = S(12);
        float w = (card.Width - 2 * inner - gap * (imgs.Length - 1)) / imgs.Length;
        for (int i = 0; i < imgs.Length; i++)
            DrawCover(g, imgs[i], new RectangleF(card.X + inner + i * (w + gap), card.Y + inner, w, card.Height - 2 * inner), S(14), 1);
    }
    public override void Reload() { LoadAll(); Redraw(); }

    protected override void Dispose(bool disposing) { if (disposing) ReleaseImages(); base.Dispose(disposing); }
}

// ---------------------------------------------------------------- clock

class ClockWidget : Widget
{
    public ClockWidget(int x, int y) : base("clock", Theme.Unit, Theme.Unit, x, y)
    {
        int minute = DateTime.Now.Minute;
        Repeat(1000, () => { if (DateTime.Now.Minute != minute) { minute = DateTime.Now.Minute; Redraw(); } });
    }

    protected override void Draw(Graphics g, RectangleF card)
    {
        DateTime now = DateTime.Now;
        FillCard(g, card);
        float x = card.X + S(15), w = card.Width - S(30);
        string time = now.ToShortTimeString();
        string period = now.ToString("tt");
        bool showPeriod = period.Length > 0 && time.EndsWith(period, StringComparison.CurrentCulture);
        if (showPeriod) time = time.Substring(0, time.Length - period.Length).TrimEnd();

        using (var f = F(11, FontStyle.Bold)) Label(g, "LOCAL TIME", f, Theme.Gold, new RectangleF(x, card.Y + S(15), w, S(18)), StringAlignment.Near);
        using (var f = D(time.Length > 5 ? 30 : 40, FontStyle.Bold)) Label(g, time, f, Theme.Text, new RectangleF(x - S(2), card.Y + S(36), w + S(4), S(52)), StringAlignment.Near);
        if (showPeriod)
            using (var f = F(12, FontStyle.Bold)) Label(g, period, f, Theme.Gold, new RectangleF(x, card.Y + S(91), w, S(19)), StringAlignment.Near);
        using (var line = new Pen(Color.FromArgb(40, Theme.Text), Math.Max(1f, S(1))))
            g.DrawLine(line, x, card.Bottom - S(44), x + w, card.Bottom - S(44));
        using (var f = F(12, FontStyle.Regular)) Label(g, now.ToString("ddd, d MMM"), f, Theme.Dim, new RectangleF(x, card.Bottom - S(34), w, S(20)), StringAlignment.Near);
    }
}

// ---------------------------------------------------------------- music

class MusicWidget : Widget
{
    string title = "", artist = "";
    bool playing;
    RectangleF prev, play, next;

    public MusicWidget(int x, int y) : base("music", Theme.Medium, Theme.Unit, x, y)
    {
        Repeat(2000, () => { if (Poll()) Redraw(); });
        if (!Preview) Poll();
    }

    bool Poll()
    {
        string t = "", a = "";
        bool on = false;
        foreach (var p in Process.GetProcessesByName("Spotify"))
        {
            string w = p.MainWindowTitle;
            if (string.IsNullOrEmpty(w) || w.StartsWith("Spotify")) continue;
            int dash = w.IndexOf(" - ");
            if (dash > 0) { a = w.Substring(0, dash); t = w.Substring(dash + 3); } else t = w;
            on = true;
        }
        bool changed = t != title || a != artist || on != playing;
        title = t; artist = a; playing = on;
        return changed;
    }

    protected override void Draw(Graphics g, RectangleF card)
    {
        FillCard(g, card);
        float m = S(14);
        var art = new RectangleF(card.X + m, card.Y + m, S(100), card.Height - 2 * m);
        using (var path = Round(art, S(14)))
        using (var b = new LinearGradientBrush(art, Color.FromArgb(74, 65, 94), Color.FromArgb(36, 33, 55), 55f))
            g.FillPath(b, path);
        using (var f = new Font(Theme.Icons, S(40), GraphicsUnit.Pixel))
        using (var b = new SolidBrush(Theme.Gold))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString("", f, b, art, sf);

        float tx = art.Right + S(16), tw = card.Right - tx - S(16);
        using (var f = F(10, FontStyle.Bold)) Label(g, playing ? "NOW PLAYING" : "MUSIC", f, Theme.Gold, new RectangleF(tx, art.Y, tw, S(16)), StringAlignment.Near);
        using (var f = D(16, FontStyle.Bold)) Label(g, playing ? title : "Nothing playing", f, Theme.Text, new RectangleF(tx, art.Y + S(18), tw, S(24)), StringAlignment.Near);
        using (var f = F(12, FontStyle.Regular)) Label(g, playing ? artist : "Open Spotify or any player", f, Theme.Dim, new RectangleF(tx, art.Y + S(42), tw, S(18)), StringAlignment.Near);

        float bs = S(38), cy = art.Bottom - bs / 2, cx = tx + S(74);
        prev = new RectangleF(cx - bs * 1.9f, cy - bs / 2, bs, bs);
        play = new RectangleF(cx - bs / 2, cy - bs / 2, bs, bs);
        next = new RectangleF(cx + bs * 0.9f, cy - bs / 2, bs, bs);
        using (var b = new SolidBrush(Color.FromArgb(36, 255, 255, 255))) g.FillEllipse(b, play);
        using (var f = new Font(Theme.Icons, S(16), GraphicsUnit.Pixel))
        using (var b = new SolidBrush(Theme.Text))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            g.DrawString("", f, b, prev, sf);
            g.DrawString(playing ? "" : "", f, b, play, sf);
            g.DrawString("", f, b, next, sf);
        }
    }

    protected override bool HandleClick(PointF p)
    {
        if (prev.Contains(p)) { Native.MediaKey(0xB1); return true; }
        if (play.Contains(p)) { Native.MediaKey(0xB3); return true; }
        if (next.Contains(p)) { Native.MediaKey(0xB0); return true; }
        return false;
    }
}

// ---------------------------------------------------------------- weather

static class WeatherArt
{
    public static string Describe(int c)
    {
        if (c == 0) return "Clear";
        if (c <= 2) return "Partly Cloudy";
        if (c == 3) return "Cloudy";
        if (c == 45 || c == 48) return "Fog";
        if (c >= 51 && c <= 57) return "Drizzle";
        if (c >= 61 && c <= 67) return "Rain";
        if (c >= 71 && c <= 77) return "Snow";
        if (c >= 80 && c <= 82) return "Showers";
        if (c >= 95) return "Thunderstorm";
        return "—";
    }

    // Small vector weather icons: sun, moon, cloud, rain, snow, storm, fog.
    public static void Icon(Graphics g, int code, bool day, RectangleF r)
    {
        float s = Math.Min(r.Width, r.Height);
        PointF c = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
        bool cloud = code >= 2;
        bool clearish = code <= 2;
        if (clearish)
        {
            PointF sc = cloud ? new PointF(c.X - s * 0.14f, c.Y - s * 0.12f) : c;
            float rr = s * (cloud ? 0.22f : 0.26f);
            if (day)
            {
                using (var p = new Pen(Color.FromArgb(255, 252, 196, 60), s * 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(p, sc.X + (float)Math.Cos(a) * rr * 1.35f, sc.Y + (float)Math.Sin(a) * rr * 1.35f, sc.X + (float)Math.Cos(a) * rr * 1.75f, sc.Y + (float)Math.Sin(a) * rr * 1.75f);
                    }
                using (var b = new SolidBrush(Color.FromArgb(255, 252, 204, 64))) g.FillEllipse(b, sc.X - rr, sc.Y - rr, rr * 2, rr * 2);
            }
            else
            {
                using (var moon = new GraphicsPath())
                using (var cut = new GraphicsPath())
                {
                    moon.AddEllipse(sc.X - rr, sc.Y - rr, rr * 2, rr * 2);
                    cut.AddEllipse(sc.X - rr * 0.35f, sc.Y - rr * 1.2f, rr * 2, rr * 2);
                    using (var region = new Region(moon))
                    {
                        region.Exclude(cut);
                        using (var b = new SolidBrush(Color.FromArgb(255, 236, 232, 210))) g.FillRegion(b, region);
                    }
                }
            }
        }
        if (cloud)
        {
            float w = s * 0.78f, h = s * 0.42f;
            float x = c.X - w / 2 + (clearish ? s * 0.08f : 0), y = c.Y - h / 2 + (clearish ? s * 0.12f : -s * 0.05f);
            Color cc = code >= 61 || code >= 95 ? Color.FromArgb(255, 200, 206, 220) : Color.FromArgb(255, 246, 246, 250);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x, y + h * 0.35f, w * 0.45f, h * 0.65f);
                path.AddEllipse(x + w * 0.22f, y, w * 0.5f, h * 0.9f);
                path.AddEllipse(x + w * 0.5f, y + h * 0.25f, w * 0.5f, h * 0.75f);
                path.AddRectangle(new RectangleF(x + w * 0.2f, y + h * 0.55f, w * 0.62f, h * 0.45f));
                using (var region = new Region(path))
                using (var b = new SolidBrush(cc)) g.FillRegion(b, region);
            }
            float by = y + h + s * 0.06f;
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82))
                using (var p = new Pen(Color.FromArgb(255, 110, 170, 255), s * 0.06f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 3; i++) g.DrawLine(p, x + w * (0.3f + i * 0.2f), by, x + w * (0.25f + i * 0.2f), by + s * 0.14f);
            else if (code >= 71 && code <= 77)
                using (var b = new SolidBrush(Color.White))
                    for (int i = 0; i < 3; i++) g.FillEllipse(b, x + w * (0.27f + i * 0.2f), by + s * 0.03f, s * 0.08f, s * 0.08f);
            else if (code >= 95)
                using (var b = new SolidBrush(Color.FromArgb(255, 252, 204, 64)))
                    g.FillPolygon(b, new[] { new PointF(x + w * 0.52f, by - s * 0.02f), new PointF(x + w * 0.40f, by + s * 0.12f), new PointF(x + w * 0.50f, by + s * 0.12f), new PointF(x + w * 0.44f, by + s * 0.24f), new PointF(x + w * 0.62f, by + s * 0.07f), new PointF(x + w * 0.52f, by + s * 0.07f) });
            else if (code == 45 || code == 48)
                using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), s * 0.05f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 2; i++) g.DrawLine(p, x + w * 0.15f, by + i * s * 0.1f, x + w * 0.85f, by + i * s * 0.1f);
        }
    }

}

class WeatherWidget : Widget
{
    string status = "Loading weather…";
    string sun = "";
    double temp = double.NaN, hi, lo;
    int code;
    bool day = true;
    readonly List<string> hours = new List<string>();
    readonly List<double> hourTemps = new List<double>();
    readonly List<int> hourCodes = new List<int>();
    readonly List<bool> hourDay = new List<bool>();
    int request;

    public WeatherWidget(int x, int y) : base("weather", Theme.Medium, Theme.Unit, x, y)
    {
        if (Preview) status = "Forecast unavailable in preview";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Repeat(30 * 60 * 1000, Fetch);
        Fetch();
    }

    public override void Reload() { Fetch(); }

    void Fetch()
    {
        int version = ++request;
        string lat = Settings.Get("lat", "23.0225"), lon = Settings.Get("lon", "72.5714");
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string url = "https://api.open-meteo.com/v1/forecast?latitude=" + lat + "&longitude=" + lon +
                             "&current=temperature_2m,weather_code,is_day&hourly=temperature_2m,weather_code,is_day" +
                             "&daily=temperature_2m_max,temperature_2m_min,sunrise,sunset&timezone=auto&forecast_days=2";
                string json;
                using (var wc = new WebClient()) json = wc.DownloadString(url);
                var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
                var cur = (Dictionary<string, object>)root["current"];
                var hourly = (Dictionary<string, object>)root["hourly"];
                var daily = (Dictionary<string, object>)root["daily"];
                var times = (object[])hourly["time"];
                var temps = (object[])hourly["temperature_2m"];
                var codes = (object[])hourly["weather_code"];
                var days = (object[])hourly["is_day"];
                DateTime now = DateTime.Parse((string)cur["time"], System.Globalization.CultureInfo.InvariantCulture);
                string nowKey = now.ToString("yyyy-MM-ddTHH", System.Globalization.CultureInfo.InvariantCulture);
                int start = 0;
                for (int i = 0; i < times.Length; i++) if (((string)times[i]).StartsWith(nowKey)) { start = i; break; }
                Publish(version, () =>
                {
                    temp = Convert.ToDouble(cur["temperature_2m"]);
                    code = Convert.ToInt32(cur["weather_code"]);
                    day = Convert.ToInt32(cur["is_day"]) == 1;
                    hi = Convert.ToDouble(((object[])daily["temperature_2m_max"])[0]);
                    lo = Convert.ToDouble(((object[])daily["temperature_2m_min"])[0]);
                    // next sun event: today's sunrise or sunset, else tomorrow's sunrise
                    var rises = (object[])daily["sunrise"];
                    var sets = (object[])daily["sunset"];
                    DateTime rise0 = DateTime.Parse((string)rises[0]), set0 = DateTime.Parse((string)sets[0]);
                    if (now < rise0) sun = "Sunrise " + rise0.ToString("h:mm tt").ToLower();
                    else if (now < set0) sun = "Sunset " + set0.ToString("h:mm tt").ToLower();
                    else sun = "Sunrise " + DateTime.Parse((string)rises[1]).ToString("h:mm tt").ToLower();
                    hours.Clear(); hourTemps.Clear(); hourCodes.Clear(); hourDay.Clear();
                    for (int i = start; i < Math.Min(times.Length, start + 6); i++)
                    {
                        DateTime t = DateTime.Parse((string)times[i]);
                        hours.Add(i == start ? "Now" : t.ToString("h tt").ToLower().Replace(" ", ""));
                        hourTemps.Add(Convert.ToDouble(temps[i]));
                        hourCodes.Add(Convert.ToInt32(codes[i]));
                        hourDay.Add(Convert.ToInt32(days[i]) == 1);
                    }
                    status = null;
                    Redraw();
                });
            }
            catch
            {
                Publish(version, () => { status = "Weather unavailable"; Redraw(); });
            }
        });
    }

    void Publish(int version, Action apply)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke((Action)(() => { if (!IsDisposed && version == request) apply(); })); }
        catch (InvalidOperationException) { }
    }

    protected override void Draw(Graphics g, RectangleF card)
    {
        FillCard(g, card);

        float x = card.X + S(18), y = card.Y + S(14), w = card.Width - S(36);
        using (var f = D(14, FontStyle.Bold)) Label(g, Settings.Get("city", "Ahmedabad"), f, Theme.Text, new RectangleF(x, y, w * 0.72f, S(20)), StringAlignment.Near);
        if (status != null)
        {
            using (var f = F(13, FontStyle.Regular)) Label(g, status, f, Theme.Dim, new RectangleF(x, y + S(36), w, S(20)), StringAlignment.Near);
            return;
        }
        using (var f = new Font("Segoe UI Variable Display Light", S(42), GraphicsUnit.Pixel))
            Label(g, Math.Round(temp) + "°", f, Color.White, new RectangleF(x - S(3), y + S(14), w * 0.5f, S(56)), StringAlignment.Near);

        WeatherArt.Icon(g, code, day, new RectangleF(card.Right - S(18) - S(26), y - S(2), S(26), S(26)));
        using (var f = F(13, FontStyle.Bold)) Label(g, WeatherArt.Describe(code), f, Color.White, new RectangleF(x, y + S(28), w, S(18)), StringAlignment.Far);
        using (var f = F(12, FontStyle.Regular)) Label(g, "High " + Math.Round(hi) + "°   Low " + Math.Round(lo) + "°", f, Theme.Dim, new RectangleF(x, y + S(46), w, S(18)), StringAlignment.Far);
        using (var f = F(10.5f, FontStyle.Regular)) Label(g, sun, f, Color.FromArgb(190, 255, 255, 255), new RectangleF(x, y + S(63), w, S(16)), StringAlignment.Far);

        float rowY = card.Bottom - S(66), colW = w / Math.Max(1, hours.Count);
        using (var line = new Pen(Color.FromArgb(40, 255, 255, 255), 1f)) g.DrawLine(line, x, rowY - S(8), x + w, rowY - S(8));
        using (var f = F(11, FontStyle.Bold))
            for (int i = 0; i < hours.Count; i++)
            {
                var col = new RectangleF(x + i * colW, rowY, colW, S(16));
                Label(g, hours[i], f, i == 0 ? Theme.Gold : Theme.Dim, col, StringAlignment.Center);
                WeatherArt.Icon(g, hourCodes[i], hourDay[i], new RectangleF(col.X + colW / 2 - S(10), col.Y + S(18), S(20), S(20)));
                Label(g, Math.Round(hourTemps[i]) + "°", f, Color.White, new RectangleF(col.X, col.Y + S(41), colW, S(16)), StringAlignment.Center);
            }
    }
}

// ---------------------------------------------------------------- calendar

// ---------------------------------------------------------------- folders

// Mac-style launcher for the desktop folders, so the desktop itself can stay clean.
class FoldersWidget : Widget
{
    static readonly string[] Names = { "AI Tools", "Developer", "Creative", "Productivity", "System & Games", "Documents", "Apps", "whitedot" };
    readonly Dictionary<string, Image> icons = new Dictionary<string, Image>();
    readonly List<RectangleF> cells = new List<RectangleF>();
    int hovered = -1;

    public FoldersWidget(int x, int y) : base("folders", Theme.Medium, Theme.Unit, x, y) { LoadIcons(); }

    void LoadIcons()
    {
        foreach (var img in icons.Values) img.Dispose();
        icons.Clear();
        string iconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), @"Wallpapers\FolderIcons");
        var map = new Dictionary<string, string>();
        string list = Path.Combine(iconDir, "icons.txt");
        if (File.Exists(list))
            foreach (string line in File.ReadAllLines(list))
            {
                string[] parts = line.Split('|');
                if (parts.Length == 2) map[parts[0].Trim()] = parts[1].Trim();
            }
        foreach (string n in Names)
        {
            string ico;
            if (!map.TryGetValue(n, out ico)) ico = "plain.ico";
            // .NET can't read PNG-compressed .ico files, so use the PNG copy next to each icon
            Image img = LoadPhoto(Path.Combine(iconDir, Path.ChangeExtension(ico, ".png")));
            if (img != null) icons[n] = img;
        }
    }

    static string FolderPath(string name) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name); }

    protected override void Draw(Graphics g, RectangleF card)
    {
        FillCard(g, card);
        cells.Clear();
        float padX = S(10), gridTop = card.Y + S(33);
        float cw = (card.Width - 2 * padX) / 4f, ch = (card.Bottom - gridTop - S(9)) / 2f;
        float icon = S(36);
        using (var f = F(11, FontStyle.Bold)) Label(g, "FOLDERS", f, Theme.Gold, new RectangleF(card.X + S(15), card.Y + S(13), card.Width - S(30), S(17)), StringAlignment.Near);
        using (var f = F(10.5f, FontStyle.Bold))
        using (var fallback = new Font(Theme.Icons, S(26), GraphicsUnit.Pixel))
            for (int i = 0; i < Names.Length; i++)
            {
                var cell = new RectangleF(card.X + padX + (i % 4) * cw, gridTop + (i / 4) * ch, cw, ch);
                cells.Add(cell);
                if (i == hovered)
                {
                    var hi = cell;
                    hi.Inflate(-S(3), -S(2));
                    using (var path = Round(hi, S(12)))
                    using (var b = new SolidBrush(Color.FromArgb(34, 255, 255, 255)))
                        g.FillPath(b, path);
                }
                Image img;
                var ir = new RectangleF(cell.X + (cell.Width - icon) / 2, cell.Y + S(3), icon, icon);
                if (icons.TryGetValue(Names[i], out img)) g.DrawImage(img, ir);
                else Label(g, "\uE8B7", fallback, Theme.Sea, ir, StringAlignment.Center);
                Label(g, Names[i] == "System & Games" ? "System" : Names[i], f, Theme.Text, new RectangleF(cell.X, ir.Bottom + S(2), cell.Width, S(16)), StringAlignment.Center);
            }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var img in icons.Values) img.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = -1;
        for (int i = 0; i < cells.Count; i++) if (cells[i].Contains(e.Location)) { h = i; break; }
        if (h != hovered) { hovered = h; Redraw(); }
    }

    protected override void OnMouseLeave(EventArgs e) { hovered = -1; base.OnMouseLeave(e); }

    protected override bool HandleClick(PointF p)
    {
        for (int i = 0; i < cells.Count; i++)
            if (cells[i].Contains(p))
            {
                string dir = FolderPath(Names[i]);
                if (Directory.Exists(dir)) Process.Start("explorer.exe", "\"" + dir + "\"");
                return true;
            }
        return false;
    }

    public override void Reload() { LoadIcons(); Redraw(); }
}

// Shows or hides the normal desktop icons, the same as right-click desktop > View > Show desktop icons.
static class DesktopIcons
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

    public static bool Visible
    {
        get
        {
            object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideIcons", 0);
            return v == null || (int)v == 0;
        }
    }

    static IntPtr DefView()
    {
        IntPtr top = IntPtr.Zero;
        while ((top = FindWindowEx(IntPtr.Zero, top, null, null)) != IntPtr.Zero)
        {
            IntPtr view = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view != IntPtr.Zero) return view;
        }
        return IntPtr.Zero;
    }

    public static void Show(bool show)
    {
        if (Visible == show) return;
        IntPtr view = DefView();
        if (view != IntPtr.Zero) SendMessage(view, 0x111, new IntPtr(0x7402), IntPtr.Zero); // toggles and saves the setting
    }
}

class CalendarWidget : Widget
{
    public CalendarWidget(int x, int y) : base("calendar", Theme.Unit, Theme.Unit, x, y)
    {
        DateTime day = DateTime.Today;
        Repeat(60 * 1000, () => { if (DateTime.Today != day) { day = DateTime.Today; Redraw(); } });
    }

    protected override void Draw(Graphics g, RectangleF card)
    {
        FillCard(g, card);
        DateTime now = DateTime.Now;
        float x = card.X + S(12), y = card.Y + S(12), w = card.Width - S(24);
        using (var f = F(11, FontStyle.Bold)) Label(g, now.ToString("MMMM").ToUpper(), f, Theme.Gold, new RectangleF(x + S(2), y, w, S(16)), StringAlignment.Near);
        float cw = w / 7f, rowH = S(17);
        string[] heads = { "M", "T", "W", "T", "F", "S", "S" };
        using (var f = F(9.5f, FontStyle.Bold))
            for (int i = 0; i < 7; i++) Label(g, heads[i], f, Theme.Dim, new RectangleF(x + i * cw, y + S(20), cw, rowH), StringAlignment.Center);
        var first = new DateTime(now.Year, now.Month, 1);
        int offset = ((int)first.DayOfWeek + 6) % 7;
        int days = DateTime.DaysInMonth(now.Year, now.Month);
        using (var f = F(10, FontStyle.Bold))
            for (int d = 1; d <= days; d++)
            {
                int cell = offset + d - 1;
                float cx = x + (cell % 7) * cw, cy = y + S(38) + (cell / 7) * rowH;
                bool today = d == now.Day;
                if (today)
                    using (var b = new SolidBrush(Theme.Gold)) g.FillEllipse(b, cx + cw / 2 - S(8.5f), cy - S(1), S(17), S(17));
                Label(g, d.ToString(), f, today ? Theme.CardBottom : (cell % 7 >= 5 ? Theme.Dim : Theme.Text), new RectangleF(cx, cy, cw, rowH), StringAlignment.Center);
            }
    }
}

// ---------------------------------------------------------------- system

class SystemWidget : Widget
{
    long lastIdle, lastTotal;
    double cpu, ram;
    string disk = "";

    public SystemWidget(int x, int y) : base("system", Theme.Unit, Theme.Unit, x, y)
    {
        Sample();
        Repeat(3000, () => { Sample(); Redraw(); });
    }

    void Sample()
    {
        long idle, kernel, user;
        if (Native.GetSystemTimes(out idle, out kernel, out user))
        {
            long total = kernel + user;
            if (lastTotal > 0 && total > lastTotal) cpu = 1 - (double)(idle - lastIdle) / (total - lastTotal);
            lastIdle = idle; lastTotal = total;
        }
        var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
        if (Native.GlobalMemoryStatusEx(ref m)) ram = m.dwMemoryLoad / 100.0;
        try { var d = new DriveInfo("C"); disk = Math.Round(d.AvailableFreeSpace / 1e9) + " GB free"; } catch { }
    }

    void Ring(Graphics g, RectangleF r, double v, Color col, string label)
    {
        using (var p = new Pen(Color.FromArgb(40, 255, 255, 255), S(6))) g.DrawEllipse(p, r);
        using (var p = new Pen(col, S(6)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            if (v > 0.005) g.DrawArc(p, r, -90, (float)(360 * Math.Min(1, v)));
        using (var f = D(13, FontStyle.Bold)) Label(g, Math.Round(v * 100) + "%", f, Theme.Text, new RectangleF(r.X - S(10), r.Y + r.Height / 2 - S(10), r.Width + S(20), S(20)), StringAlignment.Center);
        using (var f = F(9.5f, FontStyle.Bold)) Label(g, label, f, Theme.Dim, new RectangleF(r.X - S(10), r.Bottom + S(6), r.Width + S(20), S(14)), StringAlignment.Center);
    }

    protected override void Draw(Graphics g, RectangleF card)
    {
        FillCard(g, card);
        float x = card.X + S(14), y = card.Y + S(12);
        using (var f = F(11, FontStyle.Bold)) Label(g, "SYSTEM", f, Theme.Gold, new RectangleF(x, y, card.Width, S(16)), StringAlignment.Near);
        float d = S(52);
        Ring(g, new RectangleF(card.X + card.Width * 0.27f - d / 2, y + S(28), d, d), cpu, Theme.Gold, "CPU");
        Ring(g, new RectangleF(card.X + card.Width * 0.73f - d / 2, y + S(28), d, d), ram, Theme.Red, "MEMORY");
        using (var f = F(10.5f, FontStyle.Regular)) Label(g, "C:  " + disk, f, Theme.Dim, new RectangleF(card.X, card.Bottom - S(24), card.Width, S(16)), StringAlignment.Center);
    }
}

// ---------------------------------------------------------------- to-do

class TodoWidget : Widget
{
    class Item { public bool Done; public string Text; }
    readonly List<Item> items = new List<Item>();
    readonly List<RectangleF> boxes = new List<RectangleF>();
    RectangleF plus;

    public TodoWidget(int x, int y) : base("todo", Theme.Unit, Theme.Unit, x, y) { LoadItems(); }

    void LoadItems()
    {
        items.Clear();
        if (Preview || !File.Exists(Settings.TodoFile)) return;
        foreach (string line in File.ReadAllLines(Settings.TodoFile))
        {
            int bar = line.IndexOf('|');
            if (bar > 0) items.Add(new Item { Done = line[0] == '1', Text = line.Substring(bar + 1) });
        }
    }

    void SaveItems()
    {
        var lines = new List<string>();
        foreach (var it in items) lines.Add((it.Done ? "1|" : "0|") + it.Text);
        File.WriteAllLines(Settings.TodoFile, lines.ToArray());
    }

    public void Add(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        items.Add(new Item { Text = text.Trim() });
        SaveItems();
        Redraw();
    }

    public void ClearDone() { items.RemoveAll(i => i.Done); SaveItems(); Redraw(); }

    public override void Reload() { LoadItems(); Redraw(); }

    protected override void Draw(Graphics g, RectangleF card)
    {
        FillCard(g, card);
        float x = card.X + S(14), y = card.Y + S(12), w = card.Width - S(28);
        int open = items.FindAll(i => !i.Done).Count;
        using (var f = F(11, FontStyle.Bold)) Label(g, "TO-DO", f, Theme.Gold, new RectangleF(x, y, w, S(16)), StringAlignment.Near);
        using (var f = D(20, FontStyle.Bold)) Label(g, open + " open", f, Theme.Text, new RectangleF(x, y + S(17), w - S(28), S(28)), StringAlignment.Near);
        plus = new RectangleF(card.Right - S(43), y, S(31), S(31));
        using (var b = new SolidBrush(Color.FromArgb(46, 255, 255, 255))) g.FillEllipse(b, plus);
        using (var f = new Font(Theme.Icons, S(11), GraphicsUnit.Pixel))
        using (var b = new SolidBrush(Theme.Text))
        using (var c = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString("", f, b, plus, c);

        boxes.Clear();
        float rowY = y + S(48);
        int shown = 0;
        using (var f = F(12, FontStyle.Regular))
        using (var fs = F(12, FontStyle.Strikeout))
            for (int i = 0; i < items.Count; i++)
            {
                if (rowY > card.Bottom - S(30)) break;
                var box = new RectangleF(x, rowY + S(1.5f), S(14), S(14));
                boxes.Add(box);
                if (items[i].Done)
                {
                    using (var b = new SolidBrush(Theme.Red)) g.FillEllipse(b, box);
                    using (var p = new Pen(Color.White, S(1.6f)))
                        g.DrawLines(p, new[] { new PointF(box.X + S(3.2f), box.Y + S(7.2f)), new PointF(box.X + S(6f), box.Y + S(10f)), new PointF(box.X + S(10.8f), box.Y + S(4.2f)) });
                }
                else using (var p = new Pen(Color.FromArgb(170, 255, 255, 255), S(1.4f))) g.DrawEllipse(p, box);
                Label(g, items[i].Text, items[i].Done ? fs : f, items[i].Done ? Theme.Dim : Theme.Text, new RectangleF(box.Right + S(8), rowY, card.Right - box.Right - S(22), S(18)), StringAlignment.Near);
                rowY += S(24);
                shown++;
            }
        if (items.Count == 0)
            using (var f = F(12, FontStyle.Regular)) Label(g, "No tasks yet", f, Theme.Dim, new RectangleF(x, rowY, w, S(18)), StringAlignment.Near);
        else if (shown < items.Count)
            using (var f = F(10.5f, FontStyle.Bold)) Label(g, "+" + (items.Count - shown) + " more", f, Theme.Dim, new RectangleF(x, card.Bottom - S(26), w, S(16)), StringAlignment.Near);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
            for (int i = 0; i < boxes.Count; i++)
                if (e.Y >= boxes[i].Y - S(5) && e.Y < boxes[i].Y + S(19) && e.X >= boxes[i].X - S(4))
                {
                    items.RemoveAt(i);
                    SaveItems();
                    Redraw();
                    SuppressMenu = true;
                    return;
                }
        base.OnMouseDown(e);
    }

    protected override bool HandleClick(PointF p)
    {
        if (plus.Contains(p)) { AskNew(); return true; }
        for (int i = 0; i < boxes.Count; i++)
        {
            var hit = boxes[i];
            hit.Inflate(S(6), S(6));
            if (hit.Contains(p)) { items[i].Done = !items[i].Done; SaveItems(); Redraw(); return true; }
        }
        return false;
    }

    public void AskNew()
    {
        var f = new Form
        {
            FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, ShowInTaskbar = false, TopMost = true,
            BackColor = Color.FromArgb(30, 24, 44), Size = new Size((int)S(260), (int)S(44)), Location = new Point(Left + Pad, Bottom - Pad + (int)S(8))
        };
        f.Region = Region.FromHrgn(CreateRoundRectRgn(0, 0, f.Width + 1, f.Height + 1, (int)S(16), (int)S(16)));
        var box = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = f.BackColor, ForeColor = Theme.Text, Font = new Font(Theme.Body, 11f),
            Location = new Point((int)S(14), (int)S(13)), Width = f.Width - (int)S(28)
        };
        f.Controls.Add(box);
        box.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Add(box.Text); f.Close(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) f.Close();
        };
        f.Deactivate += (s, e) => f.Close();
        f.Show();
        f.Activate();
        box.Focus();
    }

    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
}

// ---------------------------------------------------------------- animated scene

// One moving piece of the scene: a click-through, per-pixel-alpha window just above the desktop,
// below the widgets. Draw() paints it; Present() moves it without repainting.
class Sprite : Form
{
    Bitmap frame;
    IntPtr memDC, dib, oldObj;
    public Action<Graphics> Paint_;
    public byte Opacity_ = 255;

    public Sprite(int w, int h)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(w, h);
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000; // layered | click-through | tool window | no-activate
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x46) // stay at the very bottom, under the widgets
        {
            var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
            wp.hwndInsertAfter = new IntPtr(1);
            wp.flags &= ~0x4u;
            Marshal.StructureToPtr(wp, m.LParam, false);
        }
        base.WndProc(ref m);
    }

    void EnsureSurface()
    {
        if (frame != null) return;
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        memDC = Native.CreateCompatibleDC(screen);
        Native.ReleaseDC(IntPtr.Zero, screen);
        var bmi = new Native.BITMAPINFOHEADER { Size = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)), Width = Width, Height = -Height, Planes = 1, BitCount = 32 };
        IntPtr bits;
        dib = Native.CreateDIBSection(memDC, ref bmi, 0, out bits, IntPtr.Zero, 0);
        oldObj = Native.SelectObject(memDC, dib);
        frame = new Bitmap(Width, Height, Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
    }

    protected override void Dispose(bool disposing)
    {
        if (frame != null) { frame.Dispose(); frame = null; }
        if (memDC != IntPtr.Zero) { Native.SelectObject(memDC, oldObj); Native.DeleteObject(dib); Native.DeleteDC(memDC); memDC = IntPtr.Zero; }
        base.Dispose(disposing);
    }

    public void Draw()
    {
        if (!Widget.Preview && !IsHandleCreated) return;
        EnsureSurface();
        using (var g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (Paint_ != null) Paint_(g);
        }
    }

    public int X_, Y_;
    public Bitmap Copy() { return frame == null ? null : new Bitmap(frame); }

    public void Present(int x, int y)
    {
        X_ = x; Y_ = y;
        if (!IsHandleCreated || frame == null) return;
        var size = new Native.SIZE { cx = Width, cy = Height };
        var src = new Native.POINT();
        var pos = new Native.POINT { X = x, Y = y };
        var blend = new Native.BLEND { Op = 0, Flags = 0, Alpha = Opacity_, Format = 1 };
        Native.UpdateLayeredWindow(Handle, IntPtr.Zero, ref pos, ref size, memDC, ref src, 0, ref blend, 2);
    }
}

// The living wallpaper: the straw hat drifts in the wind, a ship sails along the horizon in a loop,
// seagulls flap across the sky and the sun's path glitters on the water. Runs at 15 fps and pauses
// while a maximised or full-screen window covers the desktop.
static class Scene
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), @"Wallpapers\Scene");
    static readonly string FullWallpaper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), @"Wallpapers\Straw-Hat-Sunset-4K.jpg");
    static readonly string BaseWallpaper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), @"Wallpapers\Straw-Hat-Sunset-Base-4K.jpg");

    static readonly List<Sprite> sprites = new List<Sprite>();
    static System.Windows.Forms.Timer timer;
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static Image hatImg, shipImg;
    static double k, horizon, hatX, hatY, hatAX, hatAY, sunX, shipLine;
    static Sprite hat, ship, shimmer;
    static readonly List<Sprite> gulls = new List<Sprite>();
    static readonly double[][] gullData = { new[] { 0.00, 210, 1.0, 22 }, new[] { 0.23, 160, 0.75, 17 }, new[] { 0.47, 260, 0.6, 14 }, new[] { 0.66, 190, 0.85, 19 }, new[] { 0.85, 235, 0.55, 12 } };
    static readonly Random rnd = new Random(7);
    static double[][] glints;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int SystemParametersInfo(int a, int b, string c, int d);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    public static bool Enabled
    {
        get { return Settings.Get("scene", "1") == "1"; }
        set { Settings.Set("scene", value ? "1" : "0"); if (value) Start(); else Stop(); }
    }

    static void SetWallpaper(string path)
    {
        if (Widget.Preview || !File.Exists(path)) return;
        string current = (string)Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", "");
        if (string.Equals(current, path, StringComparison.OrdinalIgnoreCase)) return;
        SystemParametersInfo(20, 0, path, 3);
    }

    // "crew" = the user's own Straw Hat crew picture brought to life; "sunset" = the original drawn sunset scene.
    public static string PreviewMode;

    public static string Mode
    {
        get
        {
            string mode = PreviewMode ?? Settings.Get("sceneMode", File.Exists(CrewWallpaper) ? "crew" : "sunset");
            return mode == "crew" && File.Exists(CrewWallpaper) ? "crew" : "sunset";
        }
        set { Stop(); Settings.Set("sceneMode", value); if (Enabled) Start(); else SetWallpaper(StillWallpaper); }
    }

    static string StillWallpaper { get { return Mode == "crew" ? CrewWallpaper : FullWallpaper; } }
    public static string SnapshotWallpaper
    {
        get { return sprites.Count > 0 && Mode == "sunset" ? BaseWallpaper : StillWallpaper; }
    }
    public static bool HasCrewWallpaper { get { return File.Exists(CrewWallpaper); } }

    static readonly string CrewWallpaper = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), @"Wallpapers\Crew\crew-4K.jpg");
    static readonly List<Sprite> petals = new List<Sprite>(), twinkles = new List<Sprite>(), sprays = new List<Sprite>();
    static double[][] petalData, twinkleData, sprayData;

    // Small four-point star, drawn once; twinkling is done by changing the sprite's opacity only.
    static Sprite Star(int size, Color col)
    {
        var s = new Sprite(size, size);
        s.Paint_ = g =>
        {
            float c = size / 2f, r = size / 2f;
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { new PointF(c, 0), new PointF(c + r * 0.16f, c - r * 0.16f), new PointF(size, c), new PointF(c + r * 0.16f, c + r * 0.16f),
                                        new PointF(c, size), new PointF(c - r * 0.16f, c + r * 0.16f), new PointF(0, c), new PointF(c - r * 0.16f, c - r * 0.16f) });
                using (var b = new PathGradientBrush(path) { CenterColor = Color.FromArgb(255, 255, 255, 255), SurroundColors = new[] { Color.FromArgb(0, col) } })
                    g.FillPath(b, path);
            }
            using (var glow = new SolidBrush(Color.FromArgb(120, col))) g.FillEllipse(glow, c - r * 0.22f, c - r * 0.22f, r * 0.44f, r * 0.44f);
        };
        return s;
    }

    static void StartCrew()
    {
        SetWallpaper(CrewWallpaper);
        Rectangle screen = Screen.PrimaryScreen.Bounds;
        k = screen.Width / 1920.0;
        sprites.Clear(); petals.Clear(); twinkles.Clear(); sprays.Clear(); gulls.Clear();

        // pink petals drifting across the whole screen (as in the picture)
        petalData = new double[11][];
        for (int i = 0; i < petalData.Length; i++)
        {
            double size = (16 + rnd.NextDouble() * 14) * k;
            // start phase, speed, sway, spin, size
            petalData[i] = new[] { rnd.NextDouble(), 0.035 + rnd.NextDouble() * 0.03, 20 + rnd.NextDouble() * 40, rnd.NextDouble() * 2 - 1, size, rnd.NextDouble() * 6.28 };
            int box = (int)(size * 1.6) + 4;
            var s = new Sprite(box, box);
            double[] pd = petalData[i];
            s.Paint_ = g =>
            {
                double t = clock.Elapsed.TotalSeconds;
                g.TranslateTransform(box / 2f, box / 2f);
                g.RotateTransform((float)(t * 60 * pd[3] + pd[5] * 57));
                float w = (float)pd[4], h = (float)(pd[4] * 0.55 * (0.6 + 0.4 * Math.Abs(Math.Sin(t * 2.2 + pd[5]))));
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(-w / 2, 0, -w / 4, -h, w / 4, -h, w / 2, 0);
                    path.AddBezier(w / 2, 0, w / 4, h, -w / 4, h, -w / 2, 0);
                    using (var b = new LinearGradientBrush(new RectangleF(-w / 2, -h, w, h * 2), Color.FromArgb(235, 255, 196, 220), Color.FromArgb(235, 236, 120, 168), 90f))
                        g.FillPath(b, path);
                }
            };
            petals.Add(s);
        }

        // twinkling stars across the sky (upper right of the picture)
        twinkleData = new double[16][];
        for (int i = 0; i < twinkleData.Length; i++)
        {
            twinkleData[i] = new[] { 0.46 + rnd.NextDouble() * 0.52, 0.02 + rnd.NextDouble() * 0.34, rnd.NextDouble() * 6.28, 0.5 + rnd.NextDouble() * 1.2 };
            var s = Star((int)((14 + rnd.NextDouble() * 16) * k), Color.FromArgb(255, 255, 236, 200));
            twinkles.Add(s);
        }

        // glints on the splashing water (bottom left of the picture)
        sprayData = new double[12][];
        for (int i = 0; i < sprayData.Length; i++)
        {
            sprayData[i] = new[] { rnd.NextDouble() * 0.30, 0.74 + rnd.NextDouble() * 0.24, rnd.NextDouble() * 6.28, 1.2 + rnd.NextDouble() * 1.8 };
            sprays.Add(Star((int)((10 + rnd.NextDouble() * 12) * k), Color.FromArgb(255, 200, 235, 255)));
        }

        // white seagulls gliding over the sky
        for (int i = 0; i < 4; i++)
        {
            double size = (13 + i * 3) * k;
            var s = new Sprite((int)(size * 2.4), (int)(size * 1.6));
            double phase = i * 1.7;
            s.Paint_ = g =>
            {
                double t = clock.Elapsed.TotalSeconds;
                float flap = (float)Math.Sin(t * 4.5 + phase);
                float cx = s.Width / 2f, cy = s.Height * 0.55f, wing = (float)size;
                float tip = cy - flap * wing * 0.45f, mid = cy - wing * 0.35f - flap * wing * 0.15f;
                using (var p = new Pen(Color.FromArgb(240, 250, 250, 255), Math.Max(1.6f, (float)(size * 0.14))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawCurve(p, new[] { new PointF(cx - wing, tip), new PointF(cx - wing * 0.45f, mid), new PointF(cx, cy) });
                    g.DrawCurve(p, new[] { new PointF(cx, cy), new PointF(cx + wing * 0.45f, mid), new PointF(cx + wing, tip) });
                }
            };
            gulls.Add(s);
        }

        sprites.AddRange(twinkles);
        sprites.AddRange(sprays);
        sprites.AddRange(gulls);
        sprites.AddRange(petals);
        foreach (var s in sprites) { if (!Widget.Preview) s.Show(); s.Draw(); }
        if (Widget.Preview) return;

        timer = new System.Windows.Forms.Timer { Interval = 66 };
        timer.Tick += (s, e) => Tick();
        timer.Start();
        Tick();
    }

    static void TickCrew(double t, Rectangle screen)
    {
        int W = screen.Width, H = screen.Height;
        for (int i = 0; i < petals.Count; i++)
        {
            var pd = petalData[i];
            double p = (pd[0] + t * pd[1] * 0.25) % 1.0;          // 0..1 along the path
            double x = -60 * k + p * (W + 120 * k) + Math.Sin(t * 0.9 + pd[5]) * pd[2] * k;
            double y = -40 * k + p * (H * 0.9) + Math.Sin(t * 1.3 + pd[5] * 2) * 18 * k + (pd[5] / 6.28) * H * 0.25;
            petals[i].Draw();
            petals[i].Present((int)x, (int)y);
        }
        for (int i = 0; i < twinkles.Count; i++)
        {
            var td = twinkleData[i];
            double a = Math.Pow(Math.Max(0, Math.Sin(t * td[3] + td[2])), 3);
            twinkles[i].Opacity_ = (byte)(255 * a);
            twinkles[i].Present((int)(td[0] * W), (int)(td[1] * H));
        }
        for (int i = 0; i < sprays.Count; i++)
        {
            var gd = sprayData[i];
            double a = Math.Pow(Math.Max(0, Math.Sin(t * gd[3] + gd[2])), 4);
            sprays[i].Opacity_ = (byte)(255 * a);
            sprays[i].Present((int)(gd[0] * W), (int)(gd[1] * H));
        }
        double gx0 = 0.40 * W, gx1 = 0.97 * W;
        for (int i = 0; i < gulls.Count; i++)
        {
            double x = gx0 + ((i * 0.27 * (gx1 - gx0) + t * (16 + i * 4) * k) % (gx1 - gx0));
            double y = (0.05 + i * 0.035) * H + Math.Sin(t * 0.5 + i) * 12 * k;
            gulls[i].Draw();
            gulls[i].Present((int)x, (int)y);
        }
    }

    public static void Start()
    {
        if (timer != null) return;
        if (Mode == "crew" && File.Exists(CrewWallpaper)) { StartCrew(); return; }
        string json = Path.Combine(Dir, "scene.json");
        if (!File.Exists(json) || !File.Exists(BaseWallpaper)) return;
        var info = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(json));
        Rectangle screen = Screen.PrimaryScreen.Bounds;
        k = screen.Width / Convert.ToDouble(info["width"]);
        horizon = Convert.ToDouble(info["horizon"]) * k;
        var hc = (object[])info["hatCenter"];
        var ha = (object[])info["hatAnchor"];
        hatX = Convert.ToDouble(hc[0]) * k; hatY = Convert.ToDouble(hc[1]) * k;
        hatAX = Convert.ToDouble(ha[0]) * k; hatAY = Convert.ToDouble(ha[1]) * k;
        sunX = Convert.ToDouble(info["sunX"]) * k;
        shipLine = Convert.ToDouble(info["shipWaterline"]) * k;
        hatImg = Image.FromFile(Path.Combine(Dir, "hat.png"));
        shipImg = Image.FromFile(Path.Combine(Dir, "ship.png"));

        SetWallpaper(BaseWallpaper);

        // hat: drawn rotated each frame inside a slightly larger window
        int hw = (int)(hatImg.Width * k) + 40, hh = (int)(hatImg.Height * k) + 40;
        hat = new Sprite(hw, hh);
        hat.Paint_ = g =>
        {
            double t = clock.Elapsed.TotalSeconds;
            float angle = (float)(Math.Sin(t * 2 * Math.PI / 7) * 3.0 + Math.Sin(t * 2 * Math.PI / 2.9) * 0.8);
            g.TranslateTransform(hw / 2f, hh / 2f);
            g.RotateTransform(angle);
            g.DrawImage(hatImg, -(float)(hatImg.Width * k) / 2, -(float)(hatImg.Height * k) / 2, (float)(hatImg.Width * k), (float)(hatImg.Height * k));
        };

        // ship: static picture, only its position changes
        ship = new Sprite((int)(shipImg.Width * k) + 2, (int)(shipImg.Height * k) + 2);
        ship.Paint_ = g => g.DrawImage(shipImg, 0, 0, (float)(shipImg.Width * k), (float)(shipImg.Height * k));

        // gulls: tiny flapping wings
        foreach (var gd in gullData)
        {
            double size = gd[3] * k * 1.15;
            var s = new Sprite((int)(size * 2.4), (int)(size * 1.6));
            double phase = gd[0] * 10;
            s.Paint_ = g =>
            {
                double t = clock.Elapsed.TotalSeconds;
                float flap = (float)Math.Sin(t * 5.5 + phase);
                float cx = s.Width / 2f, cy = s.Height * 0.55f, wing = (float)size;
                float tip = cy - flap * wing * 0.45f, mid = cy - wing * 0.35f - flap * wing * 0.15f;
                using (var p = new Pen(Color.FromArgb(235, 25, 15, 35), Math.Max(1.4f, (float)(size * 0.11))) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawCurve(p, new[] { new PointF(cx - wing, tip), new PointF(cx - wing * 0.45f, mid), new PointF(cx, cy) });
                    g.DrawCurve(p, new[] { new PointF(cx, cy), new PointF(cx + wing * 0.45f, mid), new PointF(cx + wing, tip) });
                }
            };
            gulls.Add(s);
        }

        // shimmer: glints twinkling along the sun's path on the water
        int sw = (int)(420 * k), sh = screen.Height - (int)horizon - 4;
        shimmer = new Sprite(sw, Math.Max(10, sh));
        glints = new double[110][];
        for (int i = 0; i < glints.Length; i++)
        {
            double depth = Math.Pow(rnd.NextDouble(), 1.3);
            double spread = (0.12 + depth * 0.88) * sw * 0.5;
            glints[i] = new[] { sw / 2.0 + (rnd.NextDouble() * 2 - 1) * spread * rnd.NextDouble(), depth * sh, 4 + depth * 22 * k, rnd.NextDouble() * 6.28, 0.6 + rnd.NextDouble() * 1.4 };
        }
        shimmer.Paint_ = g =>
        {
            double t = clock.Elapsed.TotalSeconds;
            foreach (var gl in glints)
            {
                double a = Math.Pow(Math.Max(0, Math.Sin(t * gl[4] + gl[3])), 4);
                if (a < 0.02) continue;
                using (var p = new Pen(Color.FromArgb((int)(240 * a), 255, 236, 180), (float)(1.2 + gl[1] / sh * 1.8)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, (float)(gl[0] - gl[2] / 2), (float)gl[1], (float)(gl[0] + gl[2] / 2), (float)gl[1]);
            }
        };

        sprites.Clear();
        sprites.Add(shimmer);
        sprites.Add(ship);
        sprites.AddRange(gulls);
        sprites.Add(hat);
        if (!Widget.Preview) foreach (var s in sprites) s.Show();
        ship.Draw();
        if (Widget.Preview) return;

        timer = new System.Windows.Forms.Timer { Interval = 66 };
        timer.Tick += (s, e) => Tick();
        timer.Start();
        Tick();
    }

    public static void Stop(bool restoreWallpaper = true)
    {
        if (timer != null) { timer.Stop(); timer.Dispose(); timer = null; }
        foreach (var s in sprites) s.Dispose();
        sprites.Clear();
        gulls.Clear(); petals.Clear(); twinkles.Clear(); sprays.Clear();
        if (hatImg != null) { hatImg.Dispose(); hatImg = null; }
        if (shipImg != null) { shipImg.Dispose(); shipImg = null; }
        // the crew picture stays as a still wallpaper; the sunset scene needs its full version back (with the hat)
        if (restoreWallpaper && Mode != "crew") SetWallpaper(FullWallpaper);
    }

    // Nothing to see while a maximised or full-screen window covers the desktop, so skip the work.
    static bool Covered()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && IsZoomed(fg)) return true;
        int state;
        return SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4);
    }

    // Draws the current scene frame onto g (used by --snapshot previews).
    public static void Render(Graphics g)
    {
        if (sprites.Count == 0) return;
        Tick(true);
        foreach (var s in sprites)
            using (var b = s.Copy())
                if (b != null && s.Opacity_ > 0)
                    using (var ia = new System.Drawing.Imaging.ImageAttributes())
                    {
                        ia.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = s.Opacity_ / 255f });
                        g.DrawImage(b, new Rectangle(s.X_, s.Y_, b.Width, b.Height), 0, 0, b.Width, b.Height, GraphicsUnit.Pixel, ia);
                    }
    }

    static void Tick() { Tick(false); }

    static void Tick(bool force)
    {
        if (!force && Covered()) { timer.Interval = 500; return; }
        if (timer != null) timer.Interval = 66;
        double t = clock.Elapsed.TotalSeconds;
        Rectangle screen = Screen.PrimaryScreen.Bounds;
        if (petals.Count > 0) { TickCrew(t, screen); return; }

        // hat: gentle bob and sway, as if held up by the wind
        hat.Draw();
        double bx = Math.Sin(t * 2 * Math.PI / 9) * 10 * k, by = Math.Sin(t * 2 * Math.PI / 6) * 8 * k;
        // the picture is centred in its window, so its top-left is the anchor offset back from the original centre
        hat.Present((int)(hatX - hatAX - 20 + bx), (int)(hatY - hatAY - 20 + by));

        // ship: sails left to right between the widget columns in a 2-minute loop, fading at each end
        double x0 = 600 * k, x1 = screen.Width - 520 * k, period = 120;
        double p = (t % period) / period;
        double sx = x0 + (x1 - x0) * p;
        double fade = Math.Min(1, Math.Min(p / 0.08, (1 - p) / 0.08));
        ship.Opacity_ = (byte)(255 * Math.Max(0, fade));
        double bob = Math.Sin(t * 1.3) * 1.5 * k;
        ship.Present((int)sx, (int)(horizon - shipLine + bob));

        // gulls drift across the sky and wrap around
        double gx0 = 760 * k, gx1 = screen.Width - 230 * k;
        for (int i = 0; i < gulls.Count; i++)
        {
            var gd = gullData[i];
            double speed = 14 + gd[2] * 10;
            double x = gx0 + ((gd[0] * (gx1 - gx0) + t * speed * k) % (gx1 - gx0));
            double y = gd[1] * k + Math.Sin(t * 0.6 + i) * 10 * k;
            gulls[i].Draw();
            gulls[i].Present((int)x, (int)y);
        }

        // glitter on the water
        shimmer.Draw();
        shimmer.Present((int)(sunX - shimmer.Width / 2), (int)horizon + 4);
    }
}

// ---------------------------------------------------------------- app

static class Program
{
    static readonly List<Widget> widgets = new List<Widget>();
    static bool? previewPhotos;

    static bool ShowPhotos
    {
        get { return previewPhotos ?? (Settings.Get("photos", Scene.Mode == "crew" ? "0" : "1") == "1"); }
        set { Settings.Set("photos", value ? "1" : "0"); }
    }

    static void Build()
    {
        foreach (var w in widgets) w.Dispose();
        widgets.Clear();
        Rectangle area = Screen.PrimaryScreen.WorkingArea;
        const int margin = 24, dockClearance = 32;
        const int row = Theme.Unit + Theme.Gap;
        int minimumWidth = 2 * margin + 2 * Theme.Medium + Theme.Unit;
        int minimumHeight = 2 * margin + dockClearance + 3 * row + Theme.Unit;
        Widget.LayoutScale = Math.Min(Native.GetDpiForSystem() / 96f,
            Math.Min((float)area.Width / minimumWidth, (float)area.Height / minimumHeight));
        int width = (int)(area.Width / Widget.LayoutScale);
        int height = (int)(area.Height / Widget.LayoutScale);
        int left = margin, top = margin;
        int right = width - margin - Theme.Unit;
        int rightRail = width - margin - Theme.Medium;
        int bottom = height - margin - dockClearance - Theme.Unit;
        bool crew = Scene.Mode == "crew";

        widgets.Add(new FoldersWidget(crew ? rightRail : left, top));
        widgets.Add(new WeatherWidget(rightRail, crew ? bottom : top));
        widgets.Add(new ClockWidget(right, top + row));
        widgets.Add(new SystemWidget(crew ? right : left, top + (crew ? 2 : 3) * row));
        int tasks = crew ? bottom - row : top + row;
        widgets.Add(new TodoWidget(left, tasks));
        widgets.Add(new CalendarWidget(left + row, tasks));
        widgets.Add(new MusicWidget(left, crew ? bottom : top + 2 * row));

        if (ShowPhotos)
        {
            int px = crew ? left : rightRail;
            int py = crew ? top : top + 2 * row;
            widgets.Add(new StripWidget("strip", new[] { "photo2.jpg", "photo3.jpg", "photo4.jpg" }, px, py));
            widgets.Add(new PhotoWidget("photo-top", "photo1.jpg", false, px, py + row));
            widgets.Add(new PhotoWidget("photo-bottom", "photo5.jpg", true, px + row, py + row));
        }
        if (!Widget.Preview) foreach (var w in widgets) w.Show();
    }

    static TodoWidget Todo() { foreach (var w in widgets) if (w is TodoWidget) return (TodoWidget)w; return null; }

    [STAThread]
    static void Main()
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        bool fresh;
        using (new Mutex(true, "DeskWidgets.Single", out fresh))
        {
            string[] args = Environment.GetCommandLineArgs();
            bool snapshot = args.Length > 2 && args[1] == "--snapshot";
            if (!fresh && !snapshot) return;
            Application.EnableVisualStyles();
            Settings.Load(snapshot);
            if (snapshot)
            {
                Widget.Preview = true;
                Scene.PreviewMode = args.Length > 3 ? args[3] : null;
                if (args.Length > 4) previewPhotos = args[4] == "photos";
                SaveSnapshot(args[2]);
                return;
            }

            var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new DarkColors()), ForeColor = Theme.Text, ShowImageMargin = false };
            menu.Font = new Font(Theme.Body, 10f);
            menu.Items.Add("Add a to-do", null, (s, e) => { var t = Todo(); if (t != null) t.AskNew(); });
            menu.Items.Add("Clear finished to-dos", null, (s, e) => { var t = Todo(); if (t != null) t.ClearDone(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Change photos…", null, (s, e) => Process.Start("explorer.exe", "\"" + Settings.PhotoDir + "\""));
            menu.Items.Add("Change weather city…", null, (s, e) => Process.Start("notepad.exe", "\"" + Settings.File_ + "\""));
            menu.Items.Add("Reload", null, (s, e) => { Settings.Load(); foreach (var w in widgets) w.Reload(); });
            menu.Items.Add("Reset layout", null, (s, e) => { Settings.ClearPositions(); Build(); });
            var iconsItem = new ToolStripMenuItem("Show desktop icons") { Checked = DesktopIcons.Visible, CheckOnClick = true };
            iconsItem.CheckedChanged += (s, e) => DesktopIcons.Show(iconsItem.Checked);
            menu.Opening += (s, e) => { iconsItem.Checked = DesktopIcons.Visible; if (Widget.SuppressMenu) { e.Cancel = true; Widget.SuppressMenu = false; } };
            menu.Items.Add(iconsItem);
            var sceneItem = new ToolStripMenuItem("Animated scene") { Checked = Scene.Enabled, CheckOnClick = true };
            sceneItem.CheckedChanged += (s, e) => Scene.Enabled = sceneItem.Checked;
            menu.Items.Add(sceneItem);
            var styles = new ToolStripMenuItem("Scene style");
            var crew = new ToolStripMenuItem("Straw Hat crew (your picture)") { Checked = Scene.Mode == "crew", Enabled = Scene.HasCrewWallpaper };
            var sunset = new ToolStripMenuItem("Straw hat sunset") { Checked = Scene.Mode != "crew" };
            crew.Click += (s, e) => { Scene.Mode = "crew"; crew.Checked = true; sunset.Checked = false; Settings.ClearPositions(); Build(); };
            sunset.Click += (s, e) => { Scene.Mode = "sunset"; sunset.Checked = true; crew.Checked = false; Settings.ClearPositions(); Build(); };
            styles.DropDownItems.Add(crew);
            styles.DropDownItems.Add(sunset);
            foreach (ToolStripItem it in styles.DropDownItems) it.ForeColor = Theme.Text;
            menu.Items.Add(styles);
            var photosItem = new ToolStripMenuItem("Show photo widgets") { CheckOnClick = true };
            photosItem.CheckedChanged += (s, e) => { if (photosItem.Checked != ShowPhotos) { ShowPhotos = photosItem.Checked; Build(); } };
            menu.Opening += (s, e) => photosItem.Checked = ShowPhotos;
            menu.Items.Add(photosItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Close widgets", null, (s, e) => Application.Exit());
            foreach (ToolStripItem it in menu.Items) it.ForeColor = Theme.Text;
            Widget.SharedMenu = menu;

            Build();
            if (Scene.Enabled) Scene.Start();
            var ui = SynchronizationContext.Current;
            using (var watcher = new FileSystemWatcher(Settings.PhotoDir))
            using (var photosChanged = new System.Windows.Forms.Timer { Interval = 800 })
            using (var displayChanged = new System.Windows.Forms.Timer { Interval = 500 })
            {
                photosChanged.Tick += (s, e) =>
                {
                    photosChanged.Stop();
                    foreach (var w in widgets) if (w is PhotoWidget || w is StripWidget) w.Reload();
                };
                FileSystemEventHandler changed = (s, e) => ui.Post(_ => { photosChanged.Stop(); photosChanged.Start(); }, null);
                watcher.Changed += changed;
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Renamed += (s, e) => changed(s, e);
                watcher.EnableRaisingEvents = true;

                displayChanged.Tick += (s, e) =>
                {
                    displayChanged.Stop();
                    Build();
                    if (Scene.Enabled) { Scene.Stop(false); Scene.Start(); }
                };
                EventHandler display = (s, e) => ui.Post(_ => { displayChanged.Stop(); displayChanged.Start(); }, null);
                SystemEvents.DisplaySettingsChanged += display;
                try { Application.Run(); }
                finally
                {
                    SystemEvents.DisplaySettingsChanged -= display;
                    if (Scene.Enabled) Scene.Stop();
                }
            }
        }
    }

    static void SaveSnapshot(string path)
    {
        Build();
        try
        {
            if (Scene.Enabled) Scene.Start();
            var bounds = Screen.PrimaryScreen.Bounds;
            using (var canvas = new Bitmap(bounds.Width, bounds.Height))
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Theme.CardBottom);
                string wallpaper = Scene.SnapshotWallpaper;
                if (File.Exists(wallpaper))
                    using (var img = Image.FromFile(wallpaper))
                    {
                        float scale = Math.Max((float)bounds.Width / img.Width, (float)bounds.Height / img.Height);
                        float w = img.Width * scale, h = img.Height * scale;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, (bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h);
                    }
                Scene.Render(g);
                foreach (var widget in widgets)
                    using (var bitmap = widget.Snapshot())
                        g.DrawImageUnscaled(bitmap, widget.Left - bounds.Left, widget.Top - bounds.Top);
                canvas.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        finally
        {
            Scene.Stop();
            foreach (var widget in widgets) widget.Dispose();
        }
    }
}
