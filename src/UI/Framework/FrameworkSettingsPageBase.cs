using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.Core.Lifecycle;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;
using CS2TradeMonitor.src.UI.SettingsPage;

namespace CS2TradeMonitor.src.UI.Framework
{
    public interface ISettingsContextAwareUiPage
    {
        void SetSettingsContext(Settings config, MainForm mainForm, UIController ui);
    }

    public interface ISettingsSubRouteHost
    {
        bool SwitchSubRoute(string subRoute);
    }

    public abstract class FrameworkSettingsHostPage<TPage> : SettingsPageBase, ISettingsSubRouteHost
        where TPage : UserControl, IUiPage
    {
        private readonly PageHost _pageHost;
        private readonly SettingsTransaction _settingsTransaction;
        protected readonly TPage HostedPage;
        private bool _hosted;

        protected FrameworkSettingsHostPage(TPage page)
        {
            HostedPage = page ?? throw new ArgumentNullException(nameof(page));
            AutoScaleMode = AutoScaleMode.None;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Padding = Padding.Empty;

            _pageHost = new PageHost
            {
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _settingsTransaction = new SettingsTransaction(() => Config);
            _settingsTransaction.Draft.DraftChanged += (_, __) => NotifySettingsChanged();
            Controls.Add(_pageHost);
        }

        public override void OnShow()
        {
            base.OnShow();
            if (Config is null)
                return;

            _settingsTransaction.Rebase();
            if (HostedPage is ISettingsContextAwareUiPage contextAware)
                contextAware.SetSettingsContext(Config, MainForm, UI);

            EnsureHostLayout();
            if (!_hosted)
            {
                _pageHost.AttachSettings(_settingsTransaction.Draft);
                _pageHost.ShowPage(HostedPage);
                _hosted = true;
            }
            else
            {
                HostedPage.Activate();
            }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            EnsureHostLayout();
        }

        public override void OnThemeChanged()
        {
            base.OnThemeChanged();
            if (HostedPage is FrameworkSettingsPageBase frameworkPage)
                frameworkPage.ApplySystemTheme();
        }

        public override void RequestViewportRelayout()
        {
            base.RequestViewportRelayout();
            EnsureHostLayout();
            _pageHost.Bounds = ClientRectangle;
            _pageHost.PerformLayout();
            if (HostedPage is FrameworkSettingsPageBase frameworkPage)
            {
                frameworkPage.Dock = DockStyle.Fill;
                frameworkPage.Bounds = _pageHost.ClientRectangle;
                frameworkPage.RequestViewportRelayout();
            }
            else if (HostedPage is Control hostedControl && !hostedControl.IsDisposed)
            {
                hostedControl.Dock = DockStyle.Fill;
                hostedControl.Bounds = _pageHost.ClientRectangle;
                hostedControl.PerformLayout();
                hostedControl.Invalidate(true);
            }
        }

        public override void Save()
        {
            if (!_hosted || Config is null)
                return;

            _pageHost.SaveCurrentPage();
            _settingsTransaction.Commit();
        }

        public override void OnHide()
        {
            base.OnHide();
            if (!_hosted)
                return;

            _pageHost.SaveCurrentPage();
            HostedPage.Deactivate();
            if (Config is not null)
                _settingsTransaction.Commit();
        }

        public virtual bool SwitchSubRoute(string subRoute)
        {
            if (HostedPage is ISettingsSubRouteHost subRouteHost)
                return subRouteHost.SwitchSubRoute(subRoute);

            return false;
        }

        private void EnsureHostLayout()
        {
            if (_pageHost is null || _pageHost.IsDisposed)
                return;

            _pageHost.Dock = DockStyle.Fill;
            _pageHost.BackColor = UIColors.MainBg;
            if (HostedPage is Control hostedControl && !hostedControl.IsDisposed)
            {
                hostedControl.Dock = DockStyle.Fill;
                hostedControl.Margin = Padding.Empty;
            }
        }
    }

    public abstract class FrameworkSettingsPageBase : UserControl, IUiPage, ISettingsContextAwareUiPage
    {
        protected new readonly BufferedPanel Container;
        private readonly List<Action> _refreshActions = new List<Action>();
        private readonly List<Action> _saveActions = new List<Action>();
        private readonly List<LiteSettingsGroup> _groups = new List<LiteSettingsGroup>();
        private readonly List<IUiAsyncRefreshController> _asyncRefreshControllers = new();
        private readonly CompositeDisposable _disposables = new();
        private SettingsStore? _settingsStore;
        private UiAsyncRefreshController<string>? _storeRefreshController;
        private CancellationTokenSource? _pageCts;
        private bool _updatingControls;

        protected FrameworkSettingsPageBase()
        {
            BackColor = UIColors.MainBg;
            Dock = DockStyle.Fill;
            Padding = new Padding(0);

            Container = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = FrameworkSettingsPageLayoutHelper.CreateDefaultPagePadding(),
                BackColor = UIColors.MainBg
            };
            Container.HandleCreated += (_, __) => FrameworkSettingsPageLayoutHelper.HideHorizontalScroll(Container);
            Container.SizeChanged += (_, __) =>
            {
                FrameworkSettingsPageLayoutHelper.HideHorizontalScroll(Container);
                RelayoutGroups();
            };
            Controls.Add(Container);
        }

        protected Settings? Config { get; private set; }

        protected MainForm? MainForm { get; private set; }

        protected UIController? UI { get; private set; }

        protected bool IsUpdatingControls => _updatingControls;

        protected CancellationToken PageToken => _pageCts?.Token ?? CancellationToken.None;

        protected CompositeDisposable Disposables => _disposables;

        public virtual void SetSettingsContext(Settings config, MainForm mainForm, UIController ui)
        {
            Config = config;
            MainForm = mainForm;
            UI = ui;
        }

        public virtual void Initialize(SettingsStore settingsStore)
        {
            _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
            OnStoreAttached();
            RefreshFromStore();
        }

        public virtual void Activate()
        {
            BeginPageWork();
            QueueRefreshFromStore("Activate", immediate: true);
        }

        public virtual void Deactivate()
        {
            CancelAsyncRefreshControllers();
            CancelPageWork();
        }

        public virtual void Save()
        {
            foreach (Action saveAction in _saveActions)
                saveAction();
        }

        protected void SaveSettingsStoreToDisk()
        {
            _settingsStore?.Save();
            Config?.Save();
        }

        protected void RunIfSettingsChanged(Action action)
        {
            ArgumentNullException.ThrowIfNull(action);
            if (_settingsStore?.HasUnsavedChanges == true)
                action();
        }

        public virtual void ApplySystemTheme()
        {
            BackColor = UIColors.MainBg;
            Container.BackColor = UIColors.MainBg;
            foreach (LiteSettingsGroup group in _groups)
                group.ApplySystemTheme();

            FrameworkSettingsPageLayoutHelper.RefreshTheme(Container);
            Invalidate(true);
        }

        public virtual void RequestViewportRelayout()
        {
            Dock = DockStyle.Fill;
            Container.Dock = DockStyle.Fill;
            Container.Bounds = ClientRectangle;
            FrameworkSettingsPageLayoutHelper.StretchTopLevelContent(Container, GetTopLevelContentBounds());
            FrameworkSettingsPageLayoutHelper.HideHorizontalScroll(Container);
            RelayoutGroups();
            Container.PerformLayout();
            PerformLayout();
            Invalidate(true);
        }

        protected virtual int GetTopLevelContentWidth()
        {
            return FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(Container).Width;
        }

        protected virtual Rectangle GetTopLevelContentBounds()
        {
            return FrameworkSettingsPageLayoutHelper.CalculateTopLevelContentBounds(Container, GetTopLevelContentWidth());
        }

        protected virtual void OnStoreAttached()
        {
        }

        protected static void HideHorizontalScroll(ScrollableControl control)
        {
            FrameworkSettingsPageLayoutHelper.HideHorizontalScroll(control);
        }

        protected int GetVisibleContentWidth(
            int minimumWidth,
            int extraRightPadding = FrameworkSettingsPageLayoutHelper.DefaultContentExtraRightPadding)
        {
            return GetVisibleContentBounds(minimumWidth, extraRightPadding).Width;
        }

        protected Rectangle GetVisibleContentBounds(
            int minimumWidth,
            int extraRightPadding = FrameworkSettingsPageLayoutHelper.DefaultContentExtraRightPadding)
        {
            return FrameworkSettingsPageLayoutHelper.CalculateDefaultContentBounds(
                Container,
                minimumWidth,
                extraRightPadding);
        }

        protected Rectangle ApplyVisibleContentBounds(
            Control control,
            int minimumWidth,
            int extraRightPadding = FrameworkSettingsPageLayoutHelper.DefaultContentExtraRightPadding)
        {
            Rectangle bounds = GetVisibleContentBounds(minimumWidth, extraRightPadding);
            if (!control.IsDisposed && (control.Left != bounds.Left || control.Top != bounds.Top || control.Width != bounds.Width))
                control.SetBounds(bounds.Left, bounds.Top, bounds.Width, control.Height);

            return bounds;
        }

        protected CancellationToken BeginPageWork()
        {
            CancelPageWork();
            _pageCts = new CancellationTokenSource();
            return _pageCts.Token;
        }

        protected void CancelPageWork()
        {
            CancellationTokenSource? cts = Interlocked.Exchange(ref _pageCts, null);
            if (cts is null)
                return;

            try
            {
                cts.Cancel();
            }
            catch
            {
                // Cancellation races during theme/page switching are non-fatal.
            }
            finally
            {
                cts.Dispose();
            }
        }

        protected void RegisterRefresh(Action action)
        {
            _refreshActions.Add(action);
        }

        protected void RegisterSave(Action action)
        {
            _saveActions.Add(action);
        }

        protected UiAsyncRefreshController<TSnapshot> CreateAsyncRefreshController<TSnapshot>(
            string name,
            Func<UiRefreshReason, CancellationToken, Task<TSnapshot>> buildSnapshotAsync,
            Action<TSnapshot> applySnapshot,
            UiRefreshOptions? options = null)
        {
            var effectiveOptions = (options ?? new UiRefreshOptions { Name = name })
                .WithFallbackContext(SynchronizationContext.Current);
            var controller = new UiAsyncRefreshController<TSnapshot>(
                this,
                buildSnapshotAsync,
                applySnapshot,
                effectiveOptions);
            controller.Faulted += (_, ex) => DiagnosticsLogger.Error("Settings", $"{name} refresh failed.", ex);
            _asyncRefreshControllers.Add(controller);
            return controller;
        }

        protected void QueueRefreshFromStore(string reason = "RefreshFromStore", bool immediate = false)
        {
            if (_settingsStore is null)
                return;

            _storeRefreshController ??= CreateAsyncRefreshController<string>(
                GetType().Name + ".RefreshFromStore",
                static (refreshReason, _) => Task.FromResult(refreshReason.ToString()),
                _ => RefreshFromStore());

            UiRefreshReason refreshReason = immediate
                ? UiRefreshReason.Now(reason, GetType().Name)
                : UiRefreshReason.Deferred(reason, GetType().Name);
            _storeRefreshController.Request(refreshReason);
        }

        protected void RefreshFromStore()
        {
            RunWithUpdateGuard(() =>
            {
                foreach (Action refreshAction in _refreshActions)
                    refreshAction();
            });
        }

        protected void ClearPage()
        {
            _refreshActions.Clear();
            _saveActions.Clear();
            _groups.Clear();
            _disposables.Clear();
            while (Container.Controls.Count > 0)
            {
                Control control = Container.Controls[0];
                Container.Controls.RemoveAt(0);
                control.Dispose();
            }
        }

        protected void RunWithUpdateGuard(Action action)
        {
            bool previous = _updatingControls;
            _updatingControls = true;
            try
            {
                action();
            }
            finally
            {
                _updatingControls = previous;
            }
        }

        protected T Get<T>(string key, T fallback) where T : notnull
        {
            if (_settingsStore is not null)
                return _settingsStore.Get(key, fallback);

            if (Config is not null)
            {
                PropertyInfo? property = typeof(Settings).GetProperty(key);
                if (property?.GetValue(Config) is T typedValue)
                    return typedValue;
            }

            return fallback;
        }

        protected List<T> GetList<T>(string key)
        {
            List<T>? list = _settingsStore?.Get<List<T>?>(key, null);
            if (list is not null)
                return list;

            if (Config is not null && typeof(Settings).GetProperty(key)?.GetValue(Config) is List<T> configList)
                return configList;

            list = new List<T>();
            Set(key, list);
            return list;
        }

        protected void Set<T>(string key, T value)
        {
            _settingsStore?.Set(key, value);
            if (Config is null)
                return;

            PropertyInfo? property = typeof(Settings).GetProperty(key);
            if (property is null || !property.CanWrite)
                return;

            try
            {
                property.SetValue(Config, value);
            }
            catch
            {
                // SettingsTransaction performs the final safe conversion during Commit().
            }
        }

        protected LiteCheck AddToggle(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            bool fallback,
            Action<bool>? afterChanged = null)
        {
            var check = new LiteCheck(Get(settingKey, fallback), LanguageManager.T("Menu.Enable"));
            check.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                Set(settingKey, check.Checked);
                afterChanged?.Invoke(check.Checked);
            };
            RegisterRefresh(() => check.Checked = Get(settingKey, fallback));
            RegisterSave(() => Set(settingKey, check.Checked));
            group.AddItem(new LiteSettingsItem(TitleText(title), check));
            return check;
        }

        protected LiteUnderlineInput AddInput(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            string fallback,
            string placeholder = "",
            int width = 140,
            bool password = false,
            Action<string>? afterChanged = null)
        {
            var input = new LiteUnderlineInput(Get(settingKey, fallback), "", "", width, null, HorizontalAlignment.Left);
            if (!string.IsNullOrEmpty(placeholder))
                input.Placeholder = placeholder;
            input.Inner.UseSystemPasswordChar = password;
            input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                Set(settingKey, input.Inner.Text);
                afterChanged?.Invoke(input.Inner.Text);
            };
            RegisterRefresh(() => input.Inner.Text = Get(settingKey, fallback));
            RegisterSave(() => Set(settingKey, input.Inner.Text));
            group.AddItem(new LiteSettingsItem(TitleText(title), input));
            return input;
        }

        protected LiteNumberInput AddInt(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            int fallback,
            string unit = "",
            int width = 70,
            Func<int, int>? normalize = null,
            Action<int>? afterChanged = null)
        {
            normalize ??= value => value;
            var input = new LiteNumberInput(Get(settingKey, fallback).ToString(), unit, "", width)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (int.TryParse(input.Inner.Text, out int value))
                {
                    int normalized = normalize(value);
                    Set(settingKey, normalized);
                    afterChanged?.Invoke(normalized);
                }
            };
            RegisterRefresh(() => input.Inner.Text = normalize(Get(settingKey, fallback)).ToString());
            RegisterSave(() =>
            {
                if (int.TryParse(input.Inner.Text, out int value))
                    Set(settingKey, normalize(value));
            });
            group.AddItem(new LiteSettingsItem(TitleText(title), input));
            return input;
        }

        protected LiteNumberInput AddDouble(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            double fallback,
            string unit = "",
            int width = 70,
            Func<double, double>? normalize = null,
            Action<double>? afterChanged = null)
        {
            normalize ??= value => value;
            var input = new LiteNumberInput(Get(settingKey, fallback).ToString(), unit, "", width)
            {
                Padding = UIUtils.S(new Padding(0, 5, 0, 1))
            };
            input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                if (double.TryParse(input.Inner.Text, out double value))
                {
                    double normalized = normalize(value);
                    Set(settingKey, normalized);
                    afterChanged?.Invoke(normalized);
                }
            };
            RegisterRefresh(() => input.Inner.Text = normalize(Get(settingKey, fallback)).ToString());
            RegisterSave(() =>
            {
                if (double.TryParse(input.Inner.Text, out double value))
                    Set(settingKey, normalize(value));
            });
            group.AddItem(new LiteSettingsItem(TitleText(title), input));
            return input;
        }

        protected LiteColorInput AddColor(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            string fallback,
            Action<string>? afterChanged = null)
        {
            var input = new LiteColorInput(Get(settingKey, fallback));
            input.Input.Padding = UIUtils.S(new Padding(0, 5, 0, 1));
            input.Input.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                Set(settingKey, input.HexValue);
                afterChanged?.Invoke(input.HexValue);
            };
            RegisterRefresh(() => input.HexValue = Get(settingKey, fallback));
            RegisterSave(() => Set(settingKey, input.HexValue));
            group.AddItem(new LiteSettingsItem(TitleText(title), input));
            return input;
        }

        protected LiteComboBox AddCombo(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            IEnumerable<string> items,
            string fallback,
            Action<string>? afterChanged = null)
        {
            var combo = new LiteComboBox();
            foreach (string option in items)
                combo.Items.Add(option);

            void SelectValue(string value)
            {
                if (combo.Items.Contains(value))
                    combo.SelectedItem = value;
                else if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }

            SelectValue(Get(settingKey, fallback));
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                string value = combo.Text;
                Set(settingKey, value);
                afterChanged?.Invoke(value);
            };
            RegisterRefresh(() => SelectValue(Get(settingKey, fallback)));
            RegisterSave(() => Set(settingKey, combo.Text));
            FrameworkSettingsPageLayoutHelper.AttachAutoWidth(combo);
            group.AddItem(new LiteSettingsItem(TitleText(title), combo));
            return combo;
        }

        protected LiteComboBox AddComboIndex(
            LiteSettingsGroup group,
            string title,
            string settingKey,
            IEnumerable<string> items,
            int fallback,
            bool fullWidth = false,
            Action<int>? afterChanged = null)
        {
            var combo = new LiteComboBox();
            foreach (string option in items)
                combo.Items.Add(option);

            void SelectIndex(int index)
            {
                if (index >= 0 && index < combo.Items.Count)
                    combo.SelectedIndex = index;
                else if (combo.Items.Count > 0)
                    combo.SelectedIndex = 0;
            }

            SelectIndex(Get(settingKey, fallback));
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                int index = combo.SelectedIndex;
                if (index >= 0 && index < combo.Items.Count)
                {
                    Set(settingKey, index);
                    afterChanged?.Invoke(index);
                }
            };
            RegisterRefresh(() => SelectIndex(Get(settingKey, fallback)));
            RegisterSave(() =>
            {
                if (combo.SelectedIndex >= 0)
                    Set(settingKey, combo.SelectedIndex);
            });
            FrameworkSettingsPageLayoutHelper.AttachAutoWidth(combo);

            var item = new LiteSettingsItem(TitleText(title), combo);
            if (fullWidth)
                group.AddFullItem(item);
            else
                group.AddItem(item);
            return combo;
        }

        protected LiteHintRow AddHint(LiteSettingsGroup group, string text, int indent = 0)
        {
            var row = new LiteHintRow(text, indent);
            group.AddFullItem(row);
            return row;
        }

        protected void TrackSettingsGroup(LiteSettingsGroup group)
        {
            _groups.Add(group);
        }

        protected Panel AddGroupToPage(LiteSettingsGroup group)
        {
            TrackSettingsGroup(group);
            var wrapper = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Padding = new Padding(0, 0, 0, UIUtils.S(16)),
                BackColor = Color.Transparent
            };
            wrapper.Resize += (_, __) => FrameworkSettingsPageLayoutHelper.RelayoutGroupWrapper(wrapper, group);
            wrapper.Controls.Add(group);

            group.SizeChanged += (_, __) => FrameworkSettingsPageLayoutHelper.RelayoutGroupWrapper(wrapper, group);
            FrameworkSettingsPageLayoutHelper.RelayoutGroupWrapper(wrapper, group);

            Container.SuspendLayout();
            Container.Controls.Add(wrapper);
            Container.ResumeLayout(false);
            FrameworkSettingsPageLayoutHelper.RelayoutGroupWrapper(wrapper, group);
            QueueRelayoutGroups();
            return wrapper;
        }

        protected void RequestRelayoutGroups()
        {
            QueueRelayoutGroups();
        }

        protected void RelayoutGroupsNow()
        {
            RelayoutGroups();
        }

        protected static Label CreateLabel(string text, bool strong = false)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F, strong ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        protected static void SetControlsEnabled(IEnumerable<Control> controls, bool enabled)
        {
            foreach (Control control in controls)
                control.Enabled = enabled;
        }

        internal static string TitleText(string title)
        {
            if (title.StartsWith("Menu.", StringComparison.Ordinal))
            {
                string translated = LanguageManager.T(title);
                if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, title, StringComparison.Ordinal))
                    return translated;

                return title switch
                {
                    "Menu.Width" => "界面宽度",
                    "Menu.Scale" => "界面缩放",
                    "Menu.PanelBackgroundOpacity" => "背景透明度",
                    "Menu.TextOpacity" => "文字透明度",
                    _ => title
                };
            }

            return title;
        }

        private void QueueRelayoutGroups()
        {
            if (!IsHandleCreated || IsDisposed)
                return;

            try
            {
                BeginInvoke(new Action(RelayoutGroups));
            }
            catch
            {
                // The page may be closing while layout is queued.
            }
        }

        private void RelayoutGroups()
        {
            if (IsDisposed)
                return;

            foreach (Control control in Container.Controls)
            {
                if (control is Panel wrapper && wrapper.Controls.Count > 0 && wrapper.Controls[0] is LiteSettingsGroup group)
                    FrameworkSettingsPageLayoutHelper.RelayoutGroupWrapper(wrapper, group);
            }
        }

        private void CancelAsyncRefreshControllers()
        {
            foreach (IUiAsyncRefreshController controller in _asyncRefreshControllers)
                controller.CancelPending();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (IUiAsyncRefreshController controller in _asyncRefreshControllers)
                    controller.Dispose();

                _asyncRefreshControllers.Clear();
                CancelPageWork();
                _disposables.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
