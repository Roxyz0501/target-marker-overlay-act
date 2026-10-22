using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace TargetMarkerOverlay
{
    public sealed class OverlayForm : Form
    {
        private const int HeaderHeight = 44;
        private const int RowHeight = 48;
        private const int FooterHeight = 8;
        private const int ResizeBorder = 7;
        private const int WM_NCHITTEST = 0x84;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12,
            HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15,
            HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_TOOLWINDOW = 0x80;

        private readonly Color transparencyColor = Color.FromArgb(1, 2, 3);
        private readonly MarkerStateTracker tracker;
        private readonly Dictionary<string, Image> jobImages = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly System.Threading.Timer boundsSaveTimer;
        private PluginSettings settings;
        private List<MarkerAssignment> rows = new List<MarkerAssignment>();
        private Image markerSprite;
        private volatile bool initialized;
        private bool appliedLocked;
        private volatile bool disposing;
        private volatile bool boundsCaptureStopped;
        private int boundsSaveTimerDisposed;
        public event EventHandler BoundsChangedByUser;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public OverlayForm(PluginSettings settings, MarkerStateTracker tracker)
        {
            this.settings = settings;
            this.tracker = tracker;
            appliedLocked = settings.Locked;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = transparencyColor;
            ClientSize = new Size(340, 280);
            MinimumSize = new Size(230, 100);
            MaximumSize = new Size(760, 1100);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            TransparencyKey = transparencyColor;
            Font = new Font("Yu Gothic UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            LoadImages();
            UpdateRoundedRegion();

            boundsSaveTimer = new System.Threading.Timer(_ =>
            {
                if (!initialized || disposing || boundsCaptureStopped || Volatile.Read(ref boundsSaveTimerDisposed) != 0) return;
                BoundsChangedByUser?.Invoke(this, EventArgs.Empty);
            }, null, Timeout.Infinite, Timeout.Infinite);
            Move += OnBoundsChanged;
            tracker.StateChanged += OnStateChanged;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOOLWINDOW;
                if (settings != null && settings.Locked) cp.ExStyle |= WS_EX_TRANSPARENT;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplySettings(settings, true);
            initialized = true;
        }

        private void OnBoundsChanged(object sender, EventArgs e)
        {
            if (!initialized || disposing || boundsCaptureStopped || settings.Locked) return;
            lock (settings)
            {
                settings.Left = Left;
                settings.Top = Top;
                settings.Width = Width;
                settings.Height = Height;
            }
            try { boundsSaveTimer.Change(450, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        public void StopPendingBoundsSaveAndCapture()
        {
            boundsCaptureStopped = true;
            StopBoundsSaveTimer(true);
        }

        private void StopBoundsSaveTimer(bool publishFinalSnapshot)
        {
            if (Interlocked.Exchange(ref boundsSaveTimerDisposed, 1) != 0) return;
            using (var callbackStopped = new ManualResetEvent(false))
            {
                if (boundsSaveTimer.Dispose(callbackStopped)) callbackStopped.WaitOne();
            }
            if (publishFinalSnapshot && initialized && !disposing)
                BoundsChangedByUser?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRoundedRegion();
            Invalidate(true);
            OnBoundsChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (settings.Locked || e.Button != MouseButtons.Left || e.Y > HeaderHeight)
                return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        private void OnStateChanged(object sender, EventArgs e)
        {
            if (IsDisposed || disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => { if (!IsDisposed && !disposing) RefreshRows(); })); }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
                return;
            }
            RefreshRows();
        }

        public void ApplySettings(PluginSettings newSettings, bool restoreBounds = false)
        {
            var lockChanged = appliedLocked != newSettings.Locked;
            settings = newSettings;
            lock (settings) settings.Normalize();
            MinimumSize = new Size(settings.ShowCharacterName ? 230 : 108, 100);
            if (restoreBounds)
            {
                var desired = new Rectangle(settings.Left, settings.Top, settings.Width, settings.Height);
                if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(desired)))
                {
                    desired.X = Screen.PrimaryScreen.WorkingArea.Left + 80;
                    desired.Y = Screen.PrimaryScreen.WorkingArea.Top + 80;
                }
                Bounds = desired;
            }
            Opacity = settings.OpacityPercent / 100d;
            appliedLocked = settings.Locked;
            if (lockChanged && IsHandleCreated) RecreateHandle();
            RefreshRows();
        }

        private void RefreshRows()
        {
            rows = Sort(tracker.Snapshot()).ToList();
            var shouldShow = settings.OverlayEnabled && (!settings.HideWhenEmpty || rows.Count > 0);
            if (shouldShow)
            {
                if (!Visible) Show();
                Invalidate(true);
            }
            else Hide();
        }

        private IEnumerable<MarkerAssignment> Sort(IEnumerable<MarkerAssignment> source)
        {
            Func<MarkerAssignment, int> marker = x => settings.Priority(settings.MarkerPriorities, MarkerCatalog.Get(x.MarkerCode).Group.ToString(), 9999);
            Func<MarkerAssignment, int> role = x => settings.Priority(settings.RolePriorities, JobCatalog.Get(x.JobId).Role.ToString(), 9999);
            Func<MarkerAssignment, int> job = x => settings.Priority(settings.JobPriorities, JobCatalog.Get(x.JobId).Abbreviation, 9999);
            switch (settings.SortMode)
            {
                case SortMode.RoleFirst:
                    return source.OrderBy(role).ThenBy(job).ThenBy(marker).ThenBy(x => MarkerCatalog.Get(x.MarkerCode).Number);
                case SortMode.JobFirst:
                    return source.OrderBy(job).ThenBy(role).ThenBy(marker).ThenBy(x => MarkerCatalog.Get(x.MarkerCode).Number);
                default:
                    return source.OrderBy(marker).ThenBy(x => MarkerCatalog.Get(x.MarkerCode).Number).ThenBy(role).ThenBy(job);
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_NCHITTEST && !settings.Locked)
            {
                var raw = message.LParam.ToInt64();
                var screenPoint = new Point(unchecked((short)(raw & 0xffff)), unchecked((short)((raw >> 16) & 0xffff)));
                var point = PointToClient(screenPoint);
                var left = point.X <= ResizeBorder;
                var right = point.X >= ClientSize.Width - ResizeBorder;
                var top = point.Y <= ResizeBorder;
                var bottom = point.Y >= ClientSize.Height - ResizeBorder;
                if (left && top) { message.Result = new IntPtr(HTTOPLEFT); return; }
                if (right && top) { message.Result = new IntPtr(HTTOPRIGHT); return; }
                if (left && bottom) { message.Result = new IntPtr(HTBOTTOMLEFT); return; }
                if (right && bottom) { message.Result = new IntPtr(HTBOTTOMRIGHT); return; }
                if (left) { message.Result = new IntPtr(HTLEFT); return; }
                if (right) { message.Result = new IntPtr(HTRIGHT); return; }
                if (top) { message.Result = new IntPtr(HTTOP); return; }
                if (bottom) { message.Result = new IntPtr(HTBOTTOM); return; }
            }
            base.WndProc(ref message);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var body = new SolidBrush(BackgroundColor(244, 13, 16, 23)))
                FillRoundedRectangle(g, body, new Rectangle(0, 0, Width - 1, Height - 1), 12);
            using (var header = new LinearGradientBrush(new Rectangle(0, 0, Width, HeaderHeight),
                       BackgroundColor(250, 38, 34, 30), BackgroundColor(246, 18, 20, 27), LinearGradientMode.Horizontal))
                FillTopRoundedRectangle(g, header, new Rectangle(0, 0, Width - 1, HeaderHeight), 12);
            using (var gold = new Pen(BackgroundColor(190, 194, 155, 82)))
            {
                g.DrawLine(gold, 12, HeaderHeight - 1, Width - 12, HeaderHeight - 1);
                g.DrawLine(gold, 15, 9, Width - 15, 9);
            }
            using (var diamond = new SolidBrush(Color.FromArgb(225, 219, 180, 102)))
                g.FillPolygon(diamond, new[] { new Point(14, 18), new Point(18, 22), new Point(14, 26), new Point(10, 22) });
            using (var titleFont = new Font("Yu Gothic UI Semibold", Width < 285 ? 8.2f : 9.2f, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(Color.FromArgb(239, 221, 178)))
                g.DrawString(Width < 180 ? "TM" : Localization.Get(settings.Language, "OverlayTitle"), titleFont, titleBrush, 25, 14);

            var statusText = settings.Locked ? rows.Count.ToString() : Localization.Get(settings.Language, "Unlocked");
            if (Width >= 300)
            {
                using (var statusFont = new Font("Segoe UI", 7.2f, settings.Locked ? FontStyle.Bold : FontStyle.Regular))
                using (var statusBrush = new SolidBrush(settings.Locked ? Color.FromArgb(150, 170, 180, 195) : Color.FromArgb(218, 201, 172, 112)))
                {
                    var size = g.MeasureString(statusText, statusFont);
                    g.DrawString(statusText, statusFont, statusBrush, Width - size.Width - 14, 14);
                }
            }

            var capacity = Math.Max(0, (ClientSize.Height - HeaderHeight - FooterHeight) / RowHeight);
            var visibleRows = Math.Min(rows.Count, capacity);
            for (var i = 0; i < visibleRows; i++)
                DrawRow(g, rows[i], HeaderHeight + (i * RowHeight), i);

            if (rows.Count == 0 && !settings.HideWhenEmpty)
            {
                using (var waitFont = new Font("Yu Gothic UI", 9f))
                using (var waitBrush = new SolidBrush(Color.FromArgb(155, 168, 178, 193)))
                    g.DrawString(Localization.Get(settings.Language, "MarkerWaiting"), waitFont, waitBrush, 14, HeaderHeight + 13);
            }
            DrawResizeGrip(g);
            using (var border = new Pen(BackgroundColor(175, 161, 126, 70)))
                DrawRoundedRectangle(g, border, new Rectangle(0, 0, Width - 1, Height - 1), 12);
        }

        private void DrawRow(Graphics g, MarkerAssignment row, int y, int index)
        {
            var job = JobCatalog.Get(row.JobId);
            var marker = MarkerCatalog.Get(row.MarkerCode);
            var rect = new Rectangle(7, y + 2, Width - 14, RowHeight - 4);
            using (var background = new LinearGradientBrush(rect,
                       BackgroundColor(116, job.RoleColor), BackgroundColor(52, job.RoleColor), LinearGradientMode.Horizontal))
                FillRoundedRectangle(g, background, rect, 6);
            using (var accent = new SolidBrush(BackgroundColor(235, job.RoleColor)))
                g.FillRectangle(accent, 7, y + 8, 3, RowHeight - 16);
            if (index > 0)
            {
                using (var separator = new Pen(BackgroundColor(35, 230, 218, 188)))
                    g.DrawLine(separator, 14, y, Width - 14, y);
            }

            var jobRect = new Rectangle(14, y + 8, 30, 30);
            using (var iconBackground = new SolidBrush(Color.FromArgb(150, 8, 10, 15))) g.FillEllipse(iconBackground, jobRect);
            Image jobImage;
            if (!string.IsNullOrEmpty(job.AssetName) && jobImages.TryGetValue(job.AssetName, out jobImage))
                g.DrawImage(jobImage, jobRect);
            else
            {
                using (var f = new Font("Segoe UI", 6.5f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.White))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(job.Abbreviation, f, b, jobRect, sf);
                }
            }

            var markerRect = new Rectangle(Width - 52, y + 4, 40, 40);
            DrawMarker(g, marker, markerRect);
            var nameRight = markerRect.Left - 7;
            var nameWidth = Math.Max(20, nameRight - 51);
            if (settings.ShowCharacterName)
            {
                using (var nameFont = new Font("Yu Gothic UI Semibold", Width < 280 ? 8.3f : 9.3f, FontStyle.Bold))
                using (var labelFont = new Font("Segoe UI Semibold", 6.9f, FontStyle.Regular))
                using (var nameBrush = new SolidBrush(Color.FromArgb(250, 249, 242)))
                using (var labelBrush = new SolidBrush(Color.FromArgb(190, 206, 213, 225)))
                {
                    var nameFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
                    var displayName = settings.AnonymousMode ? "Player " + (index + 1).ToString("00") : (row.TargetName ?? "Unknown");
                    g.DrawString(displayName, nameFont, nameBrush, new RectangleF(51, y + 6, nameWidth, 22), nameFormat);
                    g.DrawString(job.Abbreviation + "  •  " + LocalizedMarkerLabel(marker), labelFont, labelBrush, new RectangleF(51, y + 27, nameWidth, 15));
                }
            }
        }

        private void DrawMarker(Graphics g, MarkerInfo marker, Rectangle destination)
        {
            var source = MarkerSource(marker.Code);
            if (markerSprite != null && !source.IsEmpty)
            {
                g.DrawImage(markerSprite, destination, source, GraphicsUnit.Pixel);
                return;
            }

            using (var fill = new SolidBrush(Color.FromArgb(225, 115, 91, 61))) g.FillEllipse(fill, destination);
            using (var text = new SolidBrush(Color.White))
            using (var font = new Font("Segoe UI", 13f, FontStyle.Bold))
            {
                var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(marker.Number.ToString(), font, text, destination, format);
            }
        }

        private string LocalizedMarkerLabel(MarkerInfo marker)
        {
            if (marker.Group == MarkerGroup.Attack || marker.Group == MarkerGroup.Bind || marker.Group == MarkerGroup.Stop)
                return Localization.Get(settings.Language, marker.Group.ToString()).ToUpperInvariant() + " " + marker.Number;
            if (marker.Group == MarkerGroup.Shape)
            {
                var key = marker.Code == 10 ? "Square" : marker.Code == 11 ? "Circle" : marker.Code == 12 ? "Plus" : "Triangle";
                return Localization.Get(settings.Language, key).ToUpperInvariant();
            }
            return marker.Label;
        }

        private static Rectangle MarkerSource(int code)
        {
            var columns = new[] { 220, 318, 416, 515, 613 };
            var rows = new[] { 111, 216, 316, 415 };
            int column, row;
            if (code >= 0 && code <= 4) { column = code; row = 0; }
            else if (code >= 14 && code <= 16) { column = code - 14; row = 1; }
            else if (code >= 5 && code <= 6) { column = code - 2; row = 1; }
            else if (code == 7) { column = 0; row = 2; }
            else if (code >= 8 && code <= 9) { column = code - 7; row = 2; }
            else if (code >= 10 && code <= 11) { column = code - 7; row = 2; }
            else if (code >= 12 && code <= 13) { column = code - 12; row = 3; }
            else return Rectangle.Empty;
            return new Rectangle(columns[column], rows[row], 76, 76);
        }

        private void DrawResizeGrip(Graphics g)
        {
            var points = new[] { new Point(Width - 28, Height - 1), new Point(Width - 1, Height - 28), new Point(Width - 1, Height - 1) };
            using (var fill = new SolidBrush(Color.FromArgb(58, 214, 190, 132)))
                g.FillPolygon(fill, points);
            using (var pen = new Pen(Color.FromArgb(145, 229, 207, 151), 1f))
            {
                g.DrawLine(pen, Width - 14, Height - 6, Width - 6, Height - 14);
                g.DrawLine(pen, Width - 10, Height - 6, Width - 6, Height - 10);
            }
        }

        private Color BackgroundColor(int alpha, int red, int green, int blue)
        {
            return Color.FromArgb(alpha * settings.BackgroundOpacityPercent / 100, red, green, blue);
        }

        private Color BackgroundColor(int alpha, Color color)
        {
            return Color.FromArgb(alpha * settings.BackgroundOpacityPercent / 100, color);
        }

        private void UpdateRoundedRegion()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            using (var path = RoundedPath(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 12))
                Region = new Region(path);
        }

        private static GraphicsPath RoundedPath(Rectangle rectangle, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void FillRoundedRectangle(Graphics g, Brush brush, Rectangle rectangle, int radius)
        { using (var path = RoundedPath(rectangle, radius)) g.FillPath(brush, path); }

        private static void DrawRoundedRectangle(Graphics g, Pen pen, Rectangle rectangle, int radius)
        { using (var path = RoundedPath(rectangle, radius)) g.DrawPath(pen, path); }

        private static void FillTopRoundedRectangle(Graphics g, Brush brush, Rectangle rectangle, int radius)
        {
            var diameter = radius * 2;
            using (var path = new GraphicsPath())
            {
                path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
                path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
                path.AddLine(rectangle.Right, rectangle.Bottom, rectangle.Left, rectangle.Bottom);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
        }

        private void LoadImages()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.IndexOf("Assets.Jobs.", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = asm.GetManifestResourceStream(name))
                    {
                        if (stream == null) continue;
                        using (var source = Image.FromStream(stream))
                        {
                            var key = Path.GetFileNameWithoutExtension(name.Substring(name.IndexOf("Assets.Jobs.", StringComparison.OrdinalIgnoreCase) + "Assets.Jobs.".Length));
                            jobImages[key] = new Bitmap(source);
                        }
                    }
                }
                else if (name.IndexOf("Assets.Markers.", StringComparison.OrdinalIgnoreCase) >= 0 && name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                {
                    using (var stream = asm.GetManifestResourceStream(name))
                    using (var source = stream == null ? null : Image.FromStream(stream))
                        if (source != null) markerSprite = new Bitmap(source);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.disposing = true;
                boundsCaptureStopped = true;
                StopBoundsSaveTimer(false);
                tracker.StateChanged -= OnStateChanged;
                foreach (var image in jobImages.Values) image.Dispose();
                markerSprite?.Dispose();
                Region?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
