using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Helpers
{
    public enum GlobalPromptKind
    {
        Info,
        Success,
        Warning,
        Error,
        Loading
    }

    public sealed class GlobalPromptRequest
    {
        public GlobalPromptRequest(
            string title,
            string message,
            GlobalPromptKind kind = GlobalPromptKind.Info,
            AppNotificationPlacement placement = AppNotificationPlacement.Desktop,
            string? source = null,
            string? dedupKey = null,
            bool playSound = false,
            IWin32Window? owner = null,
            string? actionLabel = null,
            Action? action = null,
            string? id = null)
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
            Title = string.IsNullOrWhiteSpace(title) ? "CS2交易监控" : title.Trim();
            Message = message?.Trim() ?? string.Empty;
            Kind = kind;
            Placement = placement;
            Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
            DedupKey = string.IsNullOrWhiteSpace(dedupKey) ? null : dedupKey.Trim();
            PlaySound = playSound;
            Owner = owner;
            ActionLabel = string.IsNullOrWhiteSpace(actionLabel) ? null : actionLabel.Trim();
            Action = action;
            CreatedAt = DateTime.Now;
        }

        public string Id { get; }
        public string Title { get; }
        public string Message { get; }
        public GlobalPromptKind Kind { get; }
        public AppNotificationPlacement Placement { get; }
        public string? Source { get; }
        public string? DedupKey { get; }
        public bool PlaySound { get; }
        public IWin32Window? Owner { get; }
        public string? ActionLabel { get; }
        public Action? Action { get; }
        public DateTime CreatedAt { get; }

        public GlobalPromptRequest WithId(string id)
        {
            return new GlobalPromptRequest(
                Title,
                Message,
                Kind,
                Placement,
                Source,
                DedupKey,
                PlaySound,
                Owner,
                ActionLabel,
                Action,
                id);
        }
    }

    internal sealed class GlobalNotificationQueueState
    {
        public const int MaxVisible = 3;

        private readonly List<GlobalPromptRequest> _visible = new();
        private readonly List<GlobalPromptRequest> _pending = new();

        public IReadOnlyList<GlobalPromptRequest> Visible => _visible;
        public IReadOnlyList<GlobalPromptRequest> Pending => _pending;

        public int VisibleCount => _visible.Count;
        public int PendingCount => _pending.Count;

        public void AddOrUpdate(GlobalPromptRequest request)
        {
            if (!string.IsNullOrWhiteSpace(request.DedupKey))
            {
                int visibleIndex = _visible.FindIndex(x => string.Equals(x.DedupKey, request.DedupKey, StringComparison.OrdinalIgnoreCase));
                if (visibleIndex >= 0)
                {
                    _visible[visibleIndex] = request.WithId(_visible[visibleIndex].Id);
                    return;
                }

                int pendingIndex = _pending.FindIndex(x => string.Equals(x.DedupKey, request.DedupKey, StringComparison.OrdinalIgnoreCase));
                if (pendingIndex >= 0)
                {
                    _pending[pendingIndex] = request.WithId(_pending[pendingIndex].Id);
                    return;
                }
            }

            if (_visible.Count < MaxVisible)
            {
                _visible.Add(request);
                return;
            }

            _pending.Add(request);
        }

        public void Close(string id)
        {
            int index = _visible.FindIndex(x => string.Equals(x.Id, id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _visible.RemoveAt(index);
                PromotePending();
                return;
            }

            index = _pending.FindIndex(x => string.Equals(x.Id, id, StringComparison.Ordinal));
            if (index >= 0)
                _pending.RemoveAt(index);
        }

        public void Clear()
        {
            _visible.Clear();
            _pending.Clear();
        }

        private void PromotePending()
        {
            if (_visible.Count >= MaxVisible || _pending.Count == 0)
                return;

            var next = _pending[0];
            _pending.RemoveAt(0);
            _visible.Add(next);
        }
    }

    public static class GlobalPromptService
    {
        public static bool Notify(
            string title,
            string message,
            GlobalPromptKind kind = GlobalPromptKind.Info,
            string? source = null,
            string? dedupKey = null,
            bool playSound = false,
            AppNotificationPlacement placement = AppNotificationPlacement.Desktop,
            IWin32Window? owner = null,
            string? actionLabel = null,
            Action? action = null,
            bool respectDoNotDisturb = true)
        {
            if (respectDoNotDisturb && IsDoNotDisturbEnabled())
                return false;

            var request = new GlobalPromptRequest(
                title,
                message,
                kind,
                placement,
                source,
                dedupKey,
                playSound,
                owner,
                actionLabel,
                action);

            bool shown = GlobalNotificationHost.Show(request);
            if (shown && playSound)
                TryPlaySound(kind);

            return shown;
        }

        public static DialogResult Show(string text)
        {
            return Show(owner: null, text, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        public static DialogResult Show(string text, string caption)
        {
            return Show(owner: null, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons)
        {
            return Show(owner: null, text, caption, buttons, MessageBoxIcon.None);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(owner: null, text, caption, buttons, icon);
        }

        public static DialogResult Show(IWin32Window? owner, string text)
        {
            return Show(owner, text, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        public static DialogResult Show(IWin32Window? owner, string text, string caption)
        {
            return Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);
        }

        public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons)
        {
            return Show(owner, text, caption, buttons, MessageBoxIcon.None);
        }

        public static DialogResult Show(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            if (RequiresDecision(buttons))
                return ShowModal(owner, text, caption, buttons, icon);

            bool shown = Notify(
                caption,
                text,
                MapIcon(icon),
                source: "本机提示",
                dedupKey: BuildDedupKey(caption, text),
                playSound: icon is MessageBoxIcon.Warning or MessageBoxIcon.Error,
                owner: owner,
                respectDoNotDisturb: false);

            if (shown)
                return DialogResult.OK;

            return System.Windows.Forms.MessageBox.Show(owner, text, caption, buttons, icon);
        }

        public static DialogResult ShowModal(IWin32Window? owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            try
            {
                if (!CanShowWinForms())
                    return System.Windows.Forms.MessageBox.Show(owner, text, caption, buttons, icon);

                using var dialog = new GlobalPromptDialog(caption, text, buttons, icon);
                return owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("Notification", "ShowGlobalPromptDialog", ex, retryable: true, category: "UI");
                return System.Windows.Forms.MessageBox.Show(owner, text, caption, buttons, icon);
            }
        }

        public static GlobalPromptKind MapIcon(MessageBoxIcon icon)
        {
            if (icon == MessageBoxIcon.Error)
                return GlobalPromptKind.Error;

            if (icon == MessageBoxIcon.Warning || icon == MessageBoxIcon.Question)
                return GlobalPromptKind.Warning;

            return GlobalPromptKind.Info;
        }

        public static GlobalPromptKind MapToolTipIcon(ToolTipIcon icon)
        {
            return icon switch
            {
                ToolTipIcon.Error => GlobalPromptKind.Error,
                ToolTipIcon.Warning => GlobalPromptKind.Warning,
                ToolTipIcon.Info => GlobalPromptKind.Info,
                _ => GlobalPromptKind.Info
            };
        }

        public static GlobalPromptKind MapSeverity(AppNotificationSeverity severity)
        {
            return severity switch
            {
                AppNotificationSeverity.Warning => GlobalPromptKind.Warning,
                AppNotificationSeverity.Success => GlobalPromptKind.Success,
                AppNotificationSeverity.Error => GlobalPromptKind.Error,
                AppNotificationSeverity.Loading => GlobalPromptKind.Loading,
                _ => GlobalPromptKind.Info
            };
        }

        private static bool RequiresDecision(MessageBoxButtons buttons)
        {
            return buttons != MessageBoxButtons.OK;
        }

        private static bool CanShowWinForms()
        {
            return !SystemInformation.TerminalServerSession || Environment.UserInteractive;
        }

        private static bool IsDoNotDisturbEnabled()
        {
            try
            {
                return SettingsHelperRuntimeServices.Resolve().AppConfigState.Notifications.DoNotDisturbEnabled;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("Notification", "ReadDoNotDisturbForGlobalPrompt", ex, retryable: true, category: "UI");
                return false;
            }
        }

        private static void TryPlaySound(GlobalPromptKind kind)
        {
            try
            {
                if (kind == GlobalPromptKind.Error)
                    System.Media.SystemSounds.Hand.Play();
                else
                    System.Media.SystemSounds.Exclamation.Play();
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("Notification", "PlayGlobalPromptSound", ex, retryable: true, category: "UI");
            }
        }

        private static string BuildDedupKey(string caption, string text)
        {
            string raw = (caption ?? string.Empty).Trim() + "|" + (text ?? string.Empty).Trim();
            return raw.Length <= 180 ? raw : raw.Substring(0, 180);
        }
    }

    internal static class GlobalNotificationHost
    {
        private static readonly object Sync = new();
        private static readonly GlobalNotificationQueueState State = new();
        private static readonly Dictionary<string, GlobalNotificationForm> Forms = new(StringComparer.Ordinal);

        public static int VisibleCount
        {
            get { lock (Sync) return State.VisibleCount; }
        }

        public static int PendingCount
        {
            get { lock (Sync) return State.PendingCount; }
        }

        public static bool Show(GlobalPromptRequest request)
        {
            Control? invoker = FindInvoker(request.Owner);
            if (invoker != null && invoker.InvokeRequired)
            {
                try
                {
                    invoker.BeginInvoke(new Action(() => ShowOnUiThread(request)));
                    return true;
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("Notification", "BeginInvokeGlobalNotification", ex, retryable: true, category: "UI");
                    return false;
                }
            }

            return ShowOnUiThread(request);
        }

        internal static void ResetForTests()
        {
            lock (Sync)
            {
                foreach (var form in Forms.Values.ToArray())
                {
                    try { form.Close(); }
                    catch (Exception ex) { DiagnosticsLogger.Ignored("Notification", "CloseGlobalNotificationForTest", ex, retryable: true, category: "UI"); }
                }

                Forms.Clear();
                State.Clear();
            }
        }

        private static bool ShowOnUiThread(GlobalPromptRequest request)
        {
            try
            {
                lock (Sync)
                {
                    State.AddOrUpdate(request);
                    SyncFormsLocked();
                    RepositionFormsLocked(request.Owner);
                    UpdatePendingBadgesLocked();
                }

                return true;
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Error("Notification", "Showing global notification failed.", ex);
                return false;
            }
        }

        private static void OnClosed(string id)
        {
            lock (Sync)
            {
                Forms.Remove(id);
                State.Close(id);
                SyncFormsLocked();
                RepositionFormsLocked(owner: null);
                UpdatePendingBadgesLocked();
            }
        }

        private static void SyncFormsLocked()
        {
            var visibleIds = State.Visible.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var stale in Forms.Where(x => !visibleIds.Contains(x.Key)).Select(x => x.Value).ToArray())
            {
                try { stale.Close(); }
                catch (Exception ex) { DiagnosticsLogger.Ignored("Notification", "CloseStaleGlobalNotification", ex, retryable: true, category: "UI"); }
            }

            foreach (var request in State.Visible)
            {
                if (Forms.TryGetValue(request.Id, out var existing) && !existing.IsDisposed)
                {
                    existing.UpdateRequest(request);
                    continue;
                }

                var form = new GlobalNotificationForm(request);
                form.NotificationClosed += (_, id) => OnClosed(id);
                Forms[request.Id] = form;
                form.Show();
                form.TopMost = true;
            }
        }

        private static void RepositionFormsLocked(IWin32Window? owner)
        {
            var visible = State.Visible.ToArray();
            if (visible.Length == 0)
                return;

            Screen screen = ResolveScreen(owner);
            Rectangle area = screen.WorkingArea;
            int margin = UIUtils.S(18);
            int y = area.Bottom - margin;

            for (int i = visible.Length - 1; i >= 0; i--)
            {
                if (!Forms.TryGetValue(visible[i].Id, out var form) || form.IsDisposed)
                    continue;

                int x = visible[i].Placement == AppNotificationPlacement.BottomLeft
                    ? area.Left + margin
                    : area.Right - form.Width - margin;
                y -= form.Height;
                x = Math.Max(area.Left + margin, Math.Min(x, area.Right - form.Width - margin));
                int safeY = Math.Max(area.Top + margin, Math.Min(y, area.Bottom - form.Height - margin));
                form.Location = new Point(x, safeY);
                y = safeY - margin;
            }
        }

        private static void UpdatePendingBadgesLocked()
        {
            var visible = State.Visible.ToArray();
            for (int i = 0; i < visible.Length; i++)
            {
                if (Forms.TryGetValue(visible[i].Id, out var form) && !form.IsDisposed)
                    form.SetPendingCount(i == visible.Length - 1 ? State.PendingCount : 0);
            }
        }

        private static Screen ResolveScreen(IWin32Window? owner)
        {
            if (owner is Control control && !control.IsDisposed && control.Visible)
                return Screen.FromControl(control);

            try
            {
                return Screen.FromPoint(Cursor.Position);
            }
            catch (Exception ex)
            {
                DiagnosticsLogger.Ignored("Notification", "ResolveGlobalNotificationScreen", ex, retryable: true, category: "UI");
                return Screen.PrimaryScreen ?? Screen.AllScreens.FirstOrDefault() ?? throw new InvalidOperationException("No screen is available.");
            }
        }

        private static Control? FindInvoker(IWin32Window? owner)
        {
            if (owner is Control control && !control.IsDisposed)
                return control;

            foreach (Form form in System.Windows.Forms.Application.OpenForms)
            {
                if (!form.IsDisposed)
                    return form;
            }

            return null;
        }

        private sealed class GlobalNotificationForm : Form
        {
            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const int WS_EX_TOOLWINDOW = 0x00000080;
            private const int CS_DROPSHADOW = 0x00020000;
            private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            private const int DWMWCP_ROUND = 2;
            private readonly GlobalPromptCardControl _card;
            private bool _closedByHost;

            public GlobalNotificationForm(GlobalPromptRequest request)
            {
                Name = "GlobalNotificationForm";
                Text = request.Title;
                AccessibleName = request.Title;
                AccessibleDescription = request.Message;
                AccessibleRole = AccessibleRole.Alert;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                BackColor = UIColors.CardBg;
                Padding = new Padding(0);
                Size = new Size(UIUtils.S(500), UIUtils.S(118));

                _card = new GlobalPromptCardControl(request, notificationMode: true)
                {
                    Dock = DockStyle.Fill
                };
                _card.CloseRequested += (_, __) => Close();
                _card.ActionRequested += (_, __) =>
                {
                    try { _card.Request.Action?.Invoke(); }
                    catch (Exception ex) { DiagnosticsLogger.Ignored("Notification", "RunGlobalNotificationAction", ex, retryable: true, category: "UI"); }
                    Close();
                };
                Controls.Add(_card);
            }

            public event EventHandler<string>? NotificationClosed;

            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                    cp.ClassStyle |= CS_DROPSHADOW;
                    return cp;
                }
            }

            public void UpdateRequest(GlobalPromptRequest request)
            {
                Text = request.Title;
                AccessibleName = request.Title;
                AccessibleDescription = request.Message;
                _card.UpdateRequest(request);
            }

            public void SetPendingCount(int count)
            {
                _card.PendingCount = count;
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                TryEnableDwmRoundCorners();
                ApplyRoundedRegion();
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                ApplyRoundedRegion();
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                base.OnFormClosed(e);
                if (!_closedByHost)
                    NotificationClosed?.Invoke(this, _card.Request.Id);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _closedByHost = true;
                    Region?.Dispose();
                    Region = null;
                }

                base.Dispose(disposing);
            }

            [DllImport("dwmapi.dll")]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

            private void TryEnableDwmRoundCorners()
            {
                try
                {
                    int preference = DWMWCP_ROUND;
                    DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
                }
                catch (Exception ex)
                {
                    DiagnosticsLogger.Ignored("Notification", "EnableGlobalNotificationRoundedCorners", ex, retryable: true, category: "UI");
                }
            }

            private void ApplyRoundedRegion()
            {
                if (!IsHandleCreated || Width <= 0 || Height <= 0)
                    return;

                using var path = UIUtils.RoundRect(new Rectangle(0, 0, Width, Height), UIUtils.S(8));
                Region?.Dispose();
                Region = new Region(path);
            }
        }
    }

    internal sealed class GlobalPromptDialog : Form
    {
        private readonly GlobalPromptCardControl _card;
        private readonly MessageBoxButtons _buttons;

        public GlobalPromptDialog(string title, string message, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            _buttons = buttons;
            Text = string.IsNullOrWhiteSpace(title) ? "CS2交易监控" : title;
            AccessibleName = Text;
            AccessibleDescription = message;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            MaximizeBox = false;
            BackColor = UIColors.MainBg;
            Padding = UIUtils.S(new Padding(1));
            Size = new Size(UIUtils.S(680), UIUtils.S(238));

            var request = new GlobalPromptRequest(Text, message, GlobalPromptService.MapIcon(icon));
            _card = new GlobalPromptCardControl(request, notificationMode: false)
            {
                Dock = DockStyle.Fill,
                DialogButtons = buttons
            };
            _card.CloseRequested += (_, __) => CloseWithDefaultCancel();
            _card.DialogResultRequested += (_, result) =>
            {
                DialogResult = result;
                Close();
            };
            Controls.Add(_card);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _card.Focus();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                CloseWithDefaultCancel();
                e.Handled = true;
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (DialogResult == DialogResult.None)
                DialogResult = DefaultCancelResult();

            base.OnFormClosing(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UIColors.ApplyNativeThemeRecursively(this);
        }

        private void CloseWithDefaultCancel()
        {
            DialogResult = DefaultCancelResult();
            Close();
        }

        private DialogResult DefaultCancelResult()
        {
            return _buttons switch
            {
                MessageBoxButtons.YesNo => DialogResult.No,
                MessageBoxButtons.RetryCancel => DialogResult.Cancel,
                MessageBoxButtons.OKCancel => DialogResult.Cancel,
                MessageBoxButtons.AbortRetryIgnore => DialogResult.Ignore,
                MessageBoxButtons.YesNoCancel => DialogResult.Cancel,
                _ => DialogResult.OK
            };
        }
    }

    internal sealed class GlobalPromptCardControl : Control
    {
        private Rectangle _closeBounds;
        private Rectangle _primaryBounds;
        private Rectangle _secondaryBounds;
        private Rectangle _tertiaryBounds;
        private bool _hoverClose;
        private bool _hoverPrimary;
        private bool _hoverSecondary;
        private bool _hoverTertiary;
        private int _pendingCount;

        public GlobalPromptCardControl(GlobalPromptRequest request, bool notificationMode)
        {
            Request = request;
            NotificationMode = notificationMode;
            BackColor = UIColors.CardBg;
            TabStop = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable |
                ControlStyles.UserPaint,
                true);
        }

        public event EventHandler? CloseRequested;
        public event EventHandler? ActionRequested;
        public event EventHandler<DialogResult>? DialogResultRequested;

        public GlobalPromptRequest Request { get; private set; }
        public bool NotificationMode { get; }
        public MessageBoxButtons DialogButtons { get; set; } = MessageBoxButtons.OK;

        public int PendingCount
        {
            get => _pendingCount;
            set
            {
                int normalized = Math.Max(0, value);
                if (_pendingCount == normalized)
                    return;

                _pendingCount = normalized;
                Invalidate();
            }
        }

        public void UpdateRequest(GlobalPromptRequest request)
        {
            Request = request;
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Escape || keyData == Keys.Enter || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Enter && !NotificationMode)
            {
                DialogResultRequested?.Invoke(this, PrimaryResult());
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hoverClose = _closeBounds.Contains(e.Location);
            bool hoverPrimary = _primaryBounds.Contains(e.Location);
            bool hoverSecondary = _secondaryBounds.Contains(e.Location);
            bool hoverTertiary = _tertiaryBounds.Contains(e.Location);
            if (_hoverClose != hoverClose || _hoverPrimary != hoverPrimary || _hoverSecondary != hoverSecondary || _hoverTertiary != hoverTertiary)
            {
                _hoverClose = hoverClose;
                _hoverPrimary = hoverPrimary;
                _hoverSecondary = hoverSecondary;
                _hoverTertiary = hoverTertiary;
                Cursor = hoverClose || hoverPrimary || hoverSecondary || hoverTertiary ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverClose = _hoverPrimary = _hoverSecondary = _hoverTertiary = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left)
                return;

            if (_closeBounds.Contains(e.Location))
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (NotificationMode)
            {
                if (_primaryBounds.Contains(e.Location))
                    ActionRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (_primaryBounds.Contains(e.Location))
            {
                DialogResultRequested?.Invoke(this, PrimaryResult());
            }
            else if (_secondaryBounds.Contains(e.Location))
            {
                DialogResultRequested?.Invoke(this, SecondaryResult());
            }
            else if (_tertiaryBounds.Contains(e.Location))
            {
                DialogResultRequested?.Invoke(this, TertiaryResult());
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle bounds = ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            Color accent = AccentColor(Request.Kind);
            using (var bg = new SolidBrush(UIColors.CardBg))
            using (var border = new Pen(UIColors.Border))
            using (var path = UIUtils.RoundRect(new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1), UIUtils.S(8)))
            {
                g.FillPath(bg, path);
                g.DrawPath(border, path);
            }

            using (var rail = new SolidBrush(accent))
            {
                g.FillRectangle(rail, 0, 0, UIUtils.S(5), bounds.Height);
            }

            DrawIcon(g, accent);
            DrawClose(g);
            DrawTextContent(g);

            if (NotificationMode && HasAction())
                DrawNotificationAction(g);
            else
            {
                _primaryBounds = Rectangle.Empty;
                if (!NotificationMode)
                    DrawDialogButtons(g);
            }
        }

        private void DrawIcon(Graphics g, Color accent)
        {
            int iconSize = UIUtils.S(NotificationMode ? 46 : 42);
            int x = UIUtils.S(NotificationMode ? 24 : 20);
            int y = NotificationMode ? (Height - iconSize) / 2 : UIUtils.S(28);
            var rect = new Rectangle(x, y, iconSize, iconSize);
            using (var fill = new SolidBrush(Color.FromArgb(32, accent)))
            using (var border = new Pen(Color.FromArgb(150, accent)))
            using (var path = UIUtils.RoundRect(rect, UIUtils.S(8)))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            if (Request.Kind == GlobalPromptKind.Info)
            {
                DrawInfoGlyph(g, rect, accent);
                return;
            }

            string glyph = Request.Kind switch
            {
                GlobalPromptKind.Success => "✓",
                GlobalPromptKind.Warning => "!",
                GlobalPromptKind.Error => "×",
                GlobalPromptKind.Loading => "…",
                _ => "i"
            };
            using var font = new Font("Segoe UI", NotificationMode ? 22F : (Request.Kind == GlobalPromptKind.Loading ? 16F : 14F), FontStyle.Bold);
            using var brush = new SolidBrush(accent);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, brush, rect, format);
        }

        private void DrawInfoGlyph(Graphics g, Rectangle rect, Color accent)
        {
            int bubbleWidth = UIUtils.S(NotificationMode ? 28 : 24);
            int bubbleHeight = UIUtils.S(NotificationMode ? 22 : 18);
            var bubble = new Rectangle(
                rect.Left + (rect.Width - bubbleWidth) / 2,
                rect.Top + (rect.Height - bubbleHeight) / 2 - UIUtils.S(2),
                bubbleWidth,
                bubbleHeight);

            using var pen = new Pen(accent, UIUtils.S(2))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
            using (var bubblePath = UIUtils.RoundRect(bubble, UIUtils.S(5)))
            {
                g.DrawPath(pen, bubblePath);
            }

            int tailY = bubble.Bottom - UIUtils.S(1);
            g.DrawLine(pen, bubble.Left + UIUtils.S(11), tailY, bubble.Left + UIUtils.S(17), tailY + UIUtils.S(7));
            g.DrawLine(pen, bubble.Left + UIUtils.S(17), tailY + UIUtils.S(7), bubble.Left + UIUtils.S(20), tailY);

            var points = new[]
            {
                new Point(bubble.Left + UIUtils.S(8), bubble.Top + UIUtils.S(17)),
                new Point(bubble.Left + UIUtils.S(14), bubble.Top + UIUtils.S(13)),
                new Point(bubble.Left + UIUtils.S(20), bubble.Top + UIUtils.S(16)),
                new Point(bubble.Left + UIUtils.S(27), bubble.Top + UIUtils.S(9))
            };
            g.DrawLines(pen, points);
        }

        private void DrawClose(Graphics g)
        {
            int size = UIUtils.S(NotificationMode ? 24 : 28);
            _closeBounds = new Rectangle(Width - UIUtils.S(NotificationMode ? 14 : 16) - size, UIUtils.S(NotificationMode ? 8 : 12), size, size);
            Color color = _hoverClose ? UIColors.TextCrit : UIColors.TextSub;
            using var font = new Font("Microsoft YaHei UI", NotificationMode ? 10.5F : 12F, FontStyle.Bold);
            TextRenderer.DrawText(
                g,
                "×",
                font,
                _closeBounds,
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawTextContent(Graphics g)
        {
            int left = UIUtils.S(NotificationMode ? 88 : 78);
            int rightPad = UIUtils.S(48);
            int width = Math.Max(UIUtils.S(80), Width - left - rightPad);
            if (NotificationMode && HasAction())
                width = Math.Max(UIUtils.S(80), Width - left - UIUtils.S(122));

            var titleRect = new Rectangle(left, UIUtils.S(NotificationMode ? 24 : 26), width, UIUtils.S(NotificationMode ? 25 : 34));
            var bodyRect = new Rectangle(left, titleRect.Bottom + UIUtils.S(NotificationMode ? 0 : 4), width, UIUtils.S(NotificationMode ? 31 : 58));
            var metaRect = new Rectangle(left, Height - UIUtils.S(NotificationMode ? 36 : 78), width, UIUtils.S(22));
            using var titleFont = new Font("Microsoft YaHei UI", NotificationMode ? 12.2F : 10.5F, FontStyle.Bold);
            using var bodyFont = new Font("Microsoft YaHei UI", NotificationMode ? 9.2F : 9F, FontStyle.Regular);

            TextRenderer.DrawText(
                g,
                Request.Title,
                titleFont,
                titleRect,
                UIColors.TextMain,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(
                g,
                Request.Message,
                bodyFont,
                bodyRect,
                UIColors.TextSub,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.WordBreak);

            string meta = BuildMetaText();
            if (!string.IsNullOrWhiteSpace(meta))
            {
                DrawMetaText(g, metaRect, meta);
            }

            if (PendingCount > 0)
                DrawPendingBadge(g);
        }

        private void DrawMetaText(Graphics g, Rectangle rect, string text)
        {
            int clockSize = UIUtils.S(NotificationMode ? 13 : 12);
            var clock = new Rectangle(
                rect.Left,
                rect.Top + (rect.Height - clockSize) / 2,
                clockSize,
                clockSize);
            using var pen = new Pen(UIColors.TextDisabled, 1.4F)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            g.DrawEllipse(pen, clock);
            var center = new Point(clock.Left + clock.Width / 2, clock.Top + clock.Height / 2);
            g.DrawLine(pen, center, new Point(center.X, clock.Top + UIUtils.S(4)));
            g.DrawLine(pen, center, new Point(clock.Right - UIUtils.S(4), center.Y));

            var textRect = new Rectangle(
                clock.Right + UIUtils.S(7),
                rect.Top,
                Math.Max(0, rect.Right - clock.Right - UIUtils.S(7)),
                rect.Height);
            using var font = new Font("Microsoft YaHei UI", NotificationMode ? 8F : 8.2F, FontStyle.Regular);
            TextRenderer.DrawText(
                g,
                text,
                font,
                textRect,
                UIColors.TextDisabled,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void DrawPendingBadge(Graphics g)
        {
            string text = $"还有 {PendingCount} 条";
            int width = UIUtils.S(NotificationMode ? 76 : 82);
            int height = UIUtils.S(NotificationMode ? 22 : 24);
            var rect = new Rectangle(_closeBounds.Left - width - UIUtils.S(8), UIUtils.S(NotificationMode ? 10 : 14), width, height);
            using (var fill = new SolidBrush(Color.FromArgb(45, UIColors.Primary)))
            using (var border = new Pen(Color.FromArgb(150, UIColors.Primary)))
            using (var path = UIUtils.RoundRect(rect, UIUtils.S(12)))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            using var font = new Font("Microsoft YaHei UI", NotificationMode ? 7.8F : 8.2F, FontStyle.Bold);
            TextRenderer.DrawText(
                g,
                text,
                font,
                rect,
                UIColors.Link,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private void DrawNotificationAction(Graphics g)
        {
            int width = UIUtils.S(76);
            int height = UIUtils.S(32);
            _primaryBounds = new Rectangle(Width - UIUtils.S(28) - width, (Height - height) / 2, width, height);
            DrawButton(g, _primaryBounds, Request.ActionLabel ?? "查看", primary: true, hover: _hoverPrimary);
        }

        private void DrawDialogButtons(Graphics g)
        {
            var buttons = DialogButtonsFor(DialogButtons);
            int buttonWidth = UIUtils.S(96);
            int buttonHeight = UIUtils.S(36);
            int gap = UIUtils.S(12);
            int total = buttons.Length * buttonWidth + Math.Max(0, buttons.Length - 1) * gap;
            int x = Width - UIUtils.S(22) - total;
            int y = Height - UIUtils.S(24) - buttonHeight;
            _primaryBounds = _secondaryBounds = _tertiaryBounds = Rectangle.Empty;

            for (int i = 0; i < buttons.Length; i++)
            {
                var rect = new Rectangle(x + i * (buttonWidth + gap), y, buttonWidth, buttonHeight);
                bool primary = i == buttons.Length - 1;
                bool hover = i == buttons.Length - 1 ? _hoverPrimary : i == buttons.Length - 2 ? _hoverSecondary : _hoverTertiary;
                DrawButton(g, rect, buttons[i].Text, primary, hover);

                if (i == buttons.Length - 1)
                    _primaryBounds = rect;
                else if (i == buttons.Length - 2)
                    _secondaryBounds = rect;
                else
                    _tertiaryBounds = rect;
            }
        }

        private void DrawButton(Graphics g, Rectangle rect, string text, bool primary, bool hover)
        {
            Color fill = primary ? (hover ? UIColors.LinkHover : UIColors.Primary) : (hover ? UIColors.ControlHover : UIColors.ControlBg);
            Color border = primary ? UIColors.Primary : UIColors.Border;
            Color textColor = primary ? Color.White : UIColors.TextMain;
            using (var fillBrush = new SolidBrush(fill))
            using (var borderPen = new Pen(border))
            using (var path = UIUtils.RoundRect(rect, UIUtils.S(5)))
            {
                g.FillPath(fillBrush, path);
                g.DrawPath(borderPen, path);
            }

            using var font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            TextRenderer.DrawText(
                g,
                text,
                font,
                rect,
                textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private bool HasAction()
        {
            return !string.IsNullOrWhiteSpace(Request.ActionLabel) && Request.Action != null;
        }

        private string BuildMetaText()
        {
            string time = Request.CreatedAt.ToString("HH:mm:ss");
            return string.IsNullOrWhiteSpace(Request.Source)
                ? time
                : $"{time} · {Request.Source}";
        }

        private DialogResult PrimaryResult()
        {
            return DialogButtons switch
            {
                MessageBoxButtons.YesNo => DialogResult.Yes,
                MessageBoxButtons.YesNoCancel => DialogResult.Yes,
                MessageBoxButtons.OKCancel => DialogResult.OK,
                MessageBoxButtons.RetryCancel => DialogResult.Retry,
                MessageBoxButtons.AbortRetryIgnore => DialogResult.Ignore,
                _ => DialogResult.OK
            };
        }

        private DialogResult SecondaryResult()
        {
            return DialogButtons switch
            {
                MessageBoxButtons.YesNo => DialogResult.No,
                MessageBoxButtons.YesNoCancel => DialogResult.No,
                MessageBoxButtons.OKCancel => DialogResult.Cancel,
                MessageBoxButtons.RetryCancel => DialogResult.Cancel,
                MessageBoxButtons.AbortRetryIgnore => DialogResult.Retry,
                _ => DialogResult.Cancel
            };
        }

        private DialogResult TertiaryResult()
        {
            return DialogButtons switch
            {
                MessageBoxButtons.YesNoCancel => DialogResult.Cancel,
                MessageBoxButtons.AbortRetryIgnore => DialogResult.Abort,
                _ => DialogResult.Cancel
            };
        }

        private static (string Text, DialogResult Result)[] DialogButtonsFor(MessageBoxButtons buttons)
        {
            return buttons switch
            {
                MessageBoxButtons.OKCancel => new[] { ("取消", DialogResult.Cancel), ("确定", DialogResult.OK) },
                MessageBoxButtons.YesNo => new[] { ("否", DialogResult.No), ("是", DialogResult.Yes) },
                MessageBoxButtons.YesNoCancel => new[] { ("取消", DialogResult.Cancel), ("否", DialogResult.No), ("是", DialogResult.Yes) },
                MessageBoxButtons.RetryCancel => new[] { ("取消", DialogResult.Cancel), ("重试", DialogResult.Retry) },
                MessageBoxButtons.AbortRetryIgnore => new[] { ("中止", DialogResult.Abort), ("重试", DialogResult.Retry), ("忽略", DialogResult.Ignore) },
                _ => new[] { ("确定", DialogResult.OK) }
            };
        }

        private static Color AccentColor(GlobalPromptKind kind)
        {
            return kind switch
            {
                GlobalPromptKind.Success => UIColors.Positive,
                GlobalPromptKind.Warning => UIColors.TextWarn,
                GlobalPromptKind.Error => UIColors.TextCrit,
                GlobalPromptKind.Loading => UIColors.Primary,
                _ => UIColors.Primary
            };
        }
    }
}
