using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

class Status { public int? Pct; public string Device; public bool Connected = true; }

// Bluetooth Battery Ring - Windows tray indicator.
// Left-click the tray icon -> a small, rounded bubble with a soft shadow and a
// little tail pointing at the icon, showing the exact headset model + battery %.
// Right-click -> Refresh / Exit. The tray icon is a ring (green / red < 20%).
class TrayApp : ApplicationContext
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr hIcon);

    NotifyIcon ni;
    Timer timer;
    ToolStripMenuItem headItem;
    string script;
    IntPtr lastHicon = IntPtr.Zero;
    Status last = new Status();
    InfoForm form;

    static bool Zh { get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh"; } }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        string script = System.IO.Path.Combine(dir, "status.ps1");
        Application.Run(new TrayApp(script));
    }

    public TrayApp(string scriptPath)
    {
        this.script = scriptPath;

        var menu = new ContextMenuStrip();
        headItem = new ToolStripMenuItem(Zh ? "电量: --" : "Battery: --");
        headItem.Enabled = false;
        var refreshItem = new ToolStripMenuItem(Zh ? "刷新" : "Refresh");
        refreshItem.Click += (s, e) => RefreshBattery();
        var exitItem = new ToolStripMenuItem(Zh ? "退出" : "Exit");
        exitItem.Click += (s, e) => { ni.Visible = false; Application.Exit(); };
        menu.Items.Add(headItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(refreshItem);
        menu.Items.Add(exitItem);

        ni = new NotifyIcon();
        ni.Visible = true;
        ni.Text = Zh ? "蓝牙电量" : "Bluetooth Battery";
        ni.ContextMenuStrip = menu;
        ni.Icon = MakeIcon(null, true);
        ni.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowInfo(); };

        timer = new Timer();
        timer.Interval = 20000;
        timer.Tick += (s, e) => RefreshBattery();
        timer.Start();

        RefreshBattery();
    }

    void RefreshBattery()
    {
        Status st;
        try { st = ReadStatus(); } catch { st = new Status(); }
        last = st;
        UpdateIcon(st);
        if (form != null && !form.IsDisposed) { form.SetStatus(st); }
    }

    Status ReadStatus()
    {
        var st = new Status();
        var psi = new ProcessStartInfo();
        psi.FileName = "powershell.exe";
        psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"";
        psi.UseShellExecute = false;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.CreateNoWindow = true;
        using (var p = Process.Start(psi))
        {
            string outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(20000);
            string best = null;
            foreach (string line in outp.Split('\n'))
            {
                string t = line.Trim();
                if (t.StartsWith("{")) best = t;
            }
            if (best == null) return st;
            Match mp = Regex.Match(best, "\"percent\"\\s*:\\s*(\\d+)");
            if (mp.Success)
            {
                int v;
                if (int.TryParse(mp.Groups[1].Value, out v) && v >= 0 && v <= 100) st.Pct = v;
            }
            Match md = Regex.Match(best, "\"device\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (md.Success) { try { st.Device = Regex.Unescape(md.Groups[1].Value); } catch { st.Device = md.Groups[1].Value; } }
            Match mc = Regex.Match(best, "\"connected\"\\s*:\\s*(true|false)");
            if (mc.Success) st.Connected = (mc.Groups[1].Value == "true");
            return st;
        }
    }

    void ShowInfo()
    {
        if (form != null && !form.IsDisposed)
        {
            form.Close();
            form = null;
            return;
        }
        Point anchor = Cursor.Position;
        form = new InfoForm(last, anchor);
        form.FormClosed += (s, e) => { form = null; };
        form.ShowBubble();
    }

    Icon MakeIcon(int? pct, bool connected)
    {
        bool known = pct.HasValue;
        int p = known ? Math.Max(0, Math.Min(100, pct.Value)) : 0;
        Color c = !known ? Color.FromArgb(140, 140, 140)
                         : (!connected ? Color.FromArgb(150, 150, 150)
                                       : (p < 20 ? Color.FromArgb(239, 68, 68)
                                                 : (p < 50 ? Color.FromArgb(245, 158, 11)
                                                           : Color.FromArgb(34, 197, 94))));
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            int cx = 16, cy = 16, r = 12;
            var rect = new Rectangle(cx - r, cy - r, r * 2, r * 2);
            using (var track = new Pen(Color.FromArgb(90, 128, 128, 128), 4))
                g.DrawEllipse(track, rect);
            if (known && p > 0)
            {
                using (var pen = new Pen(c, 4))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, rect, -90f, (float)(360.0 * p / 100.0));
                }
            }
        }
        IntPtr h = bmp.GetHicon();
        bmp.Dispose();
        if (lastHicon != IntPtr.Zero) { try { DestroyIcon(lastHicon); } catch { } }
        lastHicon = h;
        return Icon.FromHandle(h);
    }

    void UpdateIcon(Status st)
    {
        int? pct = st != null ? st.Pct : null;
        bool connected = st != null ? st.Connected : true;
        bool known = pct.HasValue;
        int p = known ? Math.Max(0, Math.Min(100, pct.Value)) : 0;
        try { ni.Icon = MakeIcon(pct, connected); } catch { }
        string name = Zh ? "蓝牙电量" : "Bluetooth Battery";
        string suffix = !known ? "" : (!connected ? (Zh ? " (已断开)" : " (disconnected)") : "");
        ni.Text = known ? (name + " " + p + "%" + suffix) : (name + " --");
        if (headItem != null) headItem.Text = known ? ((Zh ? "电量: " : "Battery: ") + p + "%" + suffix) : (Zh ? "电量: --" : "Battery: --");
    }
}

// Small layered (per-pixel alpha) bubble with soft shadow + tail, shown above the
// clicked tray icon. Rendered with GDI+ so edges are smooth and there is no window
// border / white edge on a dark background.
class InfoForm : Form
{
    Status st;
    Timer autoClose;
    Timer clickWatch;
    DateTime shownAt = DateTime.MinValue;

    const int CardW = 212;
    const int CardH = 188;
    const int Shadow = 12;
    const int TailH = 11;
    const int Radius = 16;

    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_NOACTIVATE = 0x08000000;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int ULW_ALPHA = 0x02;
    const byte AC_SRC_OVER = 0x00;
    const byte AC_SRC_ALPHA = 0x01;

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObj);

    public InfoForm(Status status, Point anchor)
    {
        st = status ?? new Status();
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(20, 21, 27);

        int w = CardW + Shadow * 2;
        int h = CardH + Shadow * 2 + TailH;
        Size = new Size(w, h);

        var wa = Screen.FromPoint(anchor).WorkingArea;
        int left = anchor.X - (Shadow + CardW / 2);
        int top = anchor.Y - h - 6;
        if (left < wa.Left) left = wa.Left;
        if (left + w > wa.Right) left = wa.Right - w;
        if (top < wa.Top) top = wa.Top;
        Location = new Point(left, top);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    public void SetStatus(Status s) { st = s ?? new Status(); if (Visible) Apply(Render()); }

    public void ShowBubble()
    {
        IntPtr _ = Handle;           // create the layered window handle
        Apply(Render());             // paint with per-pixel alpha
        Show();                      // make visible
        shownAt = DateTime.UtcNow;

        autoClose = new Timer();
        autoClose.Interval = 7000;
        autoClose.Tick += (s, e) => { autoClose.Stop(); Close(); };
        autoClose.Start();

        clickWatch = new Timer();
        clickWatch.Interval = 120;
        clickWatch.Tick += (s, e) =>
        {
            if ((DateTime.UtcNow - shownAt).TotalMilliseconds > 250 && Control.MouseButtons != MouseButtons.None)
                Close();
        };
        clickWatch.Start();
    }

    void Apply(Bitmap bmp)
    {
        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = bmp.GetHbitmap(Color.FromArgb(0));
        IntPtr old = SelectObject(memDc, hBmp);
        var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
        var src = new POINT { x = 0, y = 0 };
        var dst = new POINT { x = Location.X, y = Location.Y };
        var bf = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref bf, ULW_ALPHA);
        SelectObject(memDc, old);
        DeleteObject(hBmp);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
        bmp.Dispose();
    }

    static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public Bitmap Render()
    {
        int w = CardW + Shadow * 2;
        int h = CardH + Shadow * 2 + TailH;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            var cardRect = new Rectangle(Shadow, Shadow, CardW, CardH);

            // soft drop shadow (stacked translucent rounded rects)
            for (int i = 6; i >= 1; i--)
            {
                var r = new Rectangle(cardRect.X - i + 1, cardRect.Y - i + 3, cardRect.Width + (i - 1) * 2, cardRect.Height + (i - 1) * 2);
                using (var sp = RoundRect(r, Radius + i))
                using (var sb = new SolidBrush(Color.FromArgb(7, 0, 0, 0)))
                    g.FillPath(sb, sp);
            }

            // card body (subtle vertical gradient)
            using (var bodyPath = RoundRect(cardRect, Radius))
            using (var lg = new LinearGradientBrush(cardRect, Color.FromArgb(255, 30, 31, 38), Color.FromArgb(255, 20, 21, 27), 90f))
                g.FillPath(lg, bodyPath);

            // tail (same colour as the card bottom), pointing down at the icon
            int cx = Shadow + CardW / 2;
            var tail = new Point[] {
                new Point(cx - 10, Shadow + CardH - 2),
                new Point(cx + 10, Shadow + CardH - 2),
                new Point(cx, Shadow + CardH + TailH)
            };
            using (var tb = new SolidBrush(Color.FromArgb(255, 20, 21, 27)))
                g.FillPolygon(tb, tail);

            // ring
            int rcx = Shadow + CardW / 2;
            int rcy = Shadow + 78;
            int rad = 48;
            var ringRect = new Rectangle(rcx - rad, rcy - rad, rad * 2, rad * 2);
            using (var track = new Pen(Color.FromArgb(34, 255, 255, 255), 9f))
                g.DrawEllipse(track, ringRect);

            int? pct = (st != null) ? st.Pct : null;
            int p = pct.HasValue ? Math.Max(0, Math.Min(100, pct.Value)) : 0;
            bool conn = (st != null) ? st.Connected : true;
            Color accent = !pct.HasValue ? Color.FromArgb(140, 140, 140)
                           : (!conn ? Color.FromArgb(150, 150, 150)
                                    : (p < 20 ? Color.FromArgb(239, 68, 68)
                                              : (p < 50 ? Color.FromArgb(245, 158, 11)
                                                        : Color.FromArgb(34, 197, 94))));
            if (p > 0)
            {
                using (var pen = new Pen(accent, 9f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, ringRect, -90f, (float)(360.0 * p / 100.0));
                }
            }

            bool zh = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
            string fam = "Microsoft YaHei UI";

            string num = pct.HasValue ? (p + "%") : "--";
            using (var f = new Font(fam, 30, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(accent))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(num, f, br, new RectangleF(ringRect.X, ringRect.Y, ringRect.Width, ringRect.Height), sf);
            }

            string model = (st != null && !string.IsNullOrEmpty(st.Device)) ? st.Device : (zh ? "蓝牙耳机" : "Bluetooth headset");
            using (var f = new Font(fam, 13, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Color.FromArgb(240, 240, 245)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(model, f, br, new RectangleF(Shadow + 8, Shadow + 132, CardW - 16, 20), sf);
            }

            string status = !pct.HasValue ? (zh ? "未连接" : "Disconnected")
                       : (conn ? (zh ? "已连接" : "Connected")
                               : (zh ? "已断开（显示上次电量）" : "Disconnected (last known)"));
            using (var f = new Font(fam, 11, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var br = new SolidBrush(Color.FromArgb(150, 235, 235, 245)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(status, f, br, new RectangleF(Shadow + 8, Shadow + 154, CardW - 16, 18), sf);
            }
        }
        return bmp;
    }
}
