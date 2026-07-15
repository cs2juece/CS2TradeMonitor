using CS2TradeMonitor.Application.Abstractions;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    public sealed class MainPanelSettingsPage : FrameworkSettingsPageBase
    {
        private const string FloatTab = "Float";
        private const string TaskbarTab = "Taskbar";
        private const string StyleTab = "Style";
        private const string ItemMonitorTab = "ItemMonitor";
        private const string InventoryTrendTab = "InventoryTrend";

        private readonly Dictionary<string, Panel> _tabContents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Func<Panel>> _tabContentFactories = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Panel> _tabButtons = new(StringComparer.OrdinalIgnoreCase);
        private readonly ISteamDtItemService _steamDtItemService;
        private string _activeTab = FloatTab;
        private int _stylePresetSelection;
        private TableLayoutPanel? _root;
        private Control? _contentHost;
        private int _tabActivationVersion;
        private bool _widthSyncQueued;
        private bool _refreshingItemPrice;

        private int ContentWidth
        {
            get
            {
                return ContentBounds.Width;
            }
        }

        private Rectangle ContentBounds
        {
            get
            {
                return GetVisibleContentBounds(FrameworkSettingsPageLayoutHelper.CompactContentMinimumWidth);
            }
        }

        public MainPanelSettingsPage()
            : this(ItemMonitorPageRuntimeServices.Resolve())
        {
        }

        internal MainPanelSettingsPage(ItemMonitorPageRuntimeServices runtimeServices)
        {
            ArgumentNullException.ThrowIfNull(runtimeServices);

            _steamDtItemService = runtimeServices.SteamDtItems;
            Container.SizeChanged += (_, __) => QueueDeferredWidthSync();
        }

        public string ActiveTab => _activeTab;

        protected override void OnStoreAttached()
        {
            BuildPage();
            QueueDeferredWidthSync();
        }

        public override void Activate()
        {
            base.Activate();
            if (_root is null)
                BuildPage();
            QueueDeferredWidthSync();
        }

        public override void Save()
        {
            base.Save();
            RunIfSettingsChanged(ReloadRuntimeDisplay);
        }

        private void BuildPage()
        {
            ClearPage();
            _tabContents.Clear();
            _tabContentFactories.Clear();
            _tabButtons.Clear();
            _contentHost = null;
            Rectangle bounds = ContentBounds;

            _root = new TableLayoutPanel
            {
                Dock = DockStyle.None,
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Width = bounds.Width,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 0,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _root.SetBounds(bounds.Left, bounds.Top, bounds.Width, _root.Height);
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            Container.Controls.Add(_root);

            _root.Controls.Add(CreateHeader(), 0, _root.RowCount++);
            _root.Controls.Add(CreateTabStrip(), 0, _root.RowCount++);
            _root.Controls.Add(CreateContentHost(), 0, _root.RowCount++);
            SelectTab(_activeTab, deferContentCreation: false);
        }

        private void QueueDeferredWidthSync()
        {
            if (_widthSyncQueued || !IsHandleCreated || IsDisposed)
                return;

            _widthSyncQueued = true;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    _widthSyncQueued = false;
                    if (_root is null || IsDisposed)
                        return;

                    Rectangle bounds = ContentBounds;
                    if (Math.Abs(bounds.Width - _root.Width) >= UIUtils.S(12) || _root.Left != bounds.Left || _root.Top != bounds.Top)
                        SyncContentWidth(bounds);
                }));
            }
            catch
            {
                _widthSyncQueued = false;
            }
        }

        private void SyncContentWidth(Rectangle bounds)
        {
            if (_root is null || _root.IsDisposed)
                return;

            int width = bounds.Width;
            Container.SuspendLayout();
            _root.SuspendLayout();
            try
            {
                _root.SetBounds(bounds.Left, bounds.Top, width, _root.Height);
                foreach (Control child in _root.Controls)
                    child.Width = width;

                SyncHostedPanelWidths(_root, width);
                _root.Height = Math.Max(UIUtils.S(1), _root.GetPreferredSize(new Size(width, 0)).Height);
            }
            finally
            {
                _root.ResumeLayout(true);
                Container.ResumeLayout(true);
            }

            HideHorizontalScroll(Container);
        }

        private static void SyncHostedPanelWidths(Control parent, int contentWidth)
        {
            foreach (Control child in parent.Controls)
            {
                if (child.IsDisposed)
                    continue;

                if (child.Parent == parent && parent is TableLayoutPanel { ColumnCount: 1 })
                    child.Width = contentWidth;
                else if (child is FlowLayoutPanel && child.Dock == DockStyle.Top)
                    child.Width = contentWidth;
                else if (child is RedesignCardPanel && child.Parent is FlowLayoutPanel)
                    child.Width = contentWidth;
                else if (child.Dock == DockStyle.Top && child.Parent is not RedesignCardPanel)
                    child.Width = Math.Max(1, child.Parent?.ClientSize.Width ?? child.Width);

                SyncHostedPanelWidths(child, contentWidth);
            }
        }

        private Control CreateHeader()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Width = ContentWidth,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(12)),
                Padding = Padding.Empty
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

            var titleRow = new Panel
            {
                Height = UIUtils.S(38),
                Dock = DockStyle.Top,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty
            };
            titleRow.Controls.Add(CreateLabel("界面设置", 0, 0, 136, 34, UIFonts.Bold(15.5f), UIColors.TextMain));
            titleRow.Controls.Add(CreateLabel("配置悬浮窗、任务栏和监控列表的显示方式", 158, 8, 420, 24, UIFonts.Regular(9f), UIColors.TextSub));

            var previewGrid = new TableLayoutPanel
            {
                Height = UIUtils.S(122),
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty
            };
            previewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            previewGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            previewGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            previewGrid.Controls.Add(CreateMarketPreview("悬浮窗预览", floating: true), 0, 0);
            previewGrid.Controls.Add(CreateMarketPreview("任务栏预览", floating: false), 1, 0);

            panel.Controls.Add(titleRow);
            panel.Controls.Add(previewGrid);
            return panel;
        }

        private Control CreateMarketPreview(string title, bool floating)
        {
            var card = new RedesignCardPanel(UIColors.InputBg, radius: 6)
            {
                Dock = DockStyle.Fill,
                Margin = UIUtils.S(new Padding(floating ? 0 : 8, 0, floating ? 8 : 0, 0)),
                Padding = UIUtils.S(new Padding(18, 14, 18, 14))
            };

            card.Controls.Add(CreateLabel(title, 0, 0, 260, 24, UIFonts.Bold(10.5f), UIColors.TextMain));
            if (floating)
            {
                AddMarketPreviewLine(card, "QAQ指数", "1888.00  +1.00%", 36, Color.FromArgb(255, 68, 68));
                AddMarketPreviewLine(card, "DT指数", "888.00  -1.00%", 66, Color.FromArgb(0, 204, 102));
            }
            else
            {
                AddMarketPreviewLine(card, "DT指数", "888.00  -1.00%", 36, Color.FromArgb(0, 204, 102));
                AddMarketPreviewLine(card, "QAQ指数", "1888.00  +1.00%", 66, Color.FromArgb(255, 68, 68));
            }

            return card;
        }

        private void AddMarketPreviewLine(Control parent, string label, string value, int y, Color valueColor)
        {
            parent.Controls.Add(CreateLabel(label, 0, y, 82, 28, UIFonts.Bold(11f), UIColors.TextMain));
            parent.Controls.Add(CreateLabel(value, 88, y, 260, 28, UIFonts.Bold(11f), valueColor));
        }

        private Control CreateTabStrip()
        {
            var wrapper = new Panel
            {
                Dock = DockStyle.Top,
                Width = ContentWidth,
                Height = UIUtils.S(52),
                BackColor = UIColors.MainBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(14))
            };

            var line = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 1,
                BackColor = UIColors.Border
            };
            wrapper.Controls.Add(line);

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            wrapper.Controls.Add(flow);
            wrapper.Controls.SetChildIndex(flow, 0);

            AddTab(flow, FloatTab, "悬浮窗");
            AddTab(flow, TaskbarTab, "任务栏");
            AddTab(flow, StyleTab, "字体与颜色");
            AddTab(flow, ItemMonitorTab, "单品监控");
            AddTab(flow, InventoryTrendTab, "库存涨跌");

            return wrapper;
        }

        private void AddTab(Control parent, string key, string text)
        {
            var panel = new Panel
            {
                Width = UIUtils.S(132),
                Height = UIUtils.S(50),
                BackColor = UIColors.MainBg,
                Margin = new Padding(0)
            };
            var label = CreateLabel(text, 0, 0, 132, 44, UIFonts.Regular(9.5f), UIColors.TextSub);
            label.TextAlign = ContentAlignment.MiddleCenter;
            var underline = new Panel
            {
                Height = UIUtils.S(3),
                Width = UIUtils.S(96),
                Left = UIUtils.S(18),
                Top = UIUtils.S(47),
                BackColor = UIColors.Primary
            };
            panel.Controls.Add(label);
            panel.Controls.Add(underline);
            panel.Cursor = Cursors.Hand;
            label.Cursor = Cursors.Hand;
            panel.Click += (_, __) => SelectTab(key);
            label.Click += (_, __) => SelectTab(key);
            _tabButtons[key] = panel;
            parent.Controls.Add(panel);
        }

        private Control CreateContentHost()
        {
            var host = new Panel
            {
                Dock = DockStyle.Top,
                Width = ContentWidth,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _contentHost = host;
            RegisterTabContent(FloatTab, BuildFloatContent);
            RegisterTabContent(TaskbarTab, BuildTaskbarContent);
            RegisterTabContent(StyleTab, BuildStyleContent);
            RegisterTabContent(ItemMonitorTab, BuildItemMonitorContent);
            RegisterTabContent(InventoryTrendTab, BuildInventoryTrendContent);
            return host;
        }

        private void RegisterTabContent(string key, Func<Panel> factory)
        {
            _tabContentFactories[key] = factory;
        }

        private Panel? EnsureTabContent(string key)
        {
            if (_tabContents.TryGetValue(key, out var existing))
                return existing;
            if (_contentHost == null || !_tabContentFactories.TryGetValue(key, out var factory))
                return null;

            using (UiJankProfiler.Measure("MainPanel.BuildTabContent", $"Tab={key}", thresholdMs: 1))
            {
                Panel panel = factory();
                panel.Dock = DockStyle.Top;
                panel.Visible = false;
                _tabContents[key] = panel;
                _contentHost.Controls.Add(panel);
                return panel;
            }
        }

        public void SelectTab(string key)
        {
            SelectTab(key, deferContentCreation: true);
        }

        private void SelectTab(string key, bool deferContentCreation)
        {
            _activeTab = MainPanelTabKeys.Normalize(key);
            ApplyTabSelectionVisuals();
            if (!deferContentCreation || _tabContents.ContainsKey(_activeTab) || !IsHandleCreated)
                ActivateTabContent(_activeTab);
            else
            {
                FlushTabSelectionVisuals();
                QueueTabContentActivation(_activeTab);
            }
        }

        private void FlushTabSelectionVisuals()
        {
            foreach (Panel button in _tabButtons.Values)
                button.Update();
        }

        private void QueueTabContentActivation(string key)
        {
            int version = ++_tabActivationVersion;
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || version != _tabActivationVersion || !_activeTab.Equals(key, StringComparison.OrdinalIgnoreCase))
                    return;
                ActivateTabContent(key);
            }));
        }

        private void ActivateTabContent(string key)
        {
            Panel? activeContent = EnsureTabContent(key);
            foreach (var pair in _tabContents)
                pair.Value.Visible = pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
            activeContent?.BringToFront();
        }

        private void ApplyTabSelectionVisuals()
        {
            foreach (var pair in _tabButtons)
            {
                bool active = pair.Key.Equals(_activeTab, StringComparison.OrdinalIgnoreCase);
                foreach (Control child in pair.Value.Controls)
                {
                    if (child is Label label)
                    {
                        label.ForeColor = active ? UIColors.Primary : UIColors.TextSub;
                        label.Font = active ? UIFonts.Bold(9.5f) : UIFonts.Regular(9.5f);
                    }
                    else if (child is Panel underline)
                        underline.Visible = active;
                }
            }
        }

        private Panel BuildFloatContent()
        {
            var stack = CreateStackPanel();
            stack.Controls.Add(CreateFloatBehaviorCard());
            stack.Controls.Add(CreateFloatAppearanceCard());
            stack.Controls.Add(CreateFloatSpacingCard());
            return stack;
        }

        private Panel BuildTaskbarContent()
        {
            var stack = CreateStackPanel();
            stack.Controls.Add(CreateTaskbarDisplayCard());
            stack.Controls.Add(CreateTaskbarAdvancedCard());
            stack.Controls.Add(CreateTaskbarPositionCard());
            return stack;
        }

        private Panel BuildStyleContent()
        {
            var stack = CreateStackPanel();
            stack.Controls.Add(CreateFontCard());
            stack.Controls.Add(CreateStyleSpacingCard());
            stack.Controls.Add(CreateGeneralColorCard());
            return stack;
        }

        private Panel BuildItemMonitorContent()
        {
            var stack = CreateStackPanel();
            stack.Controls.Add(CreateItemMonitorListCard());
            stack.Controls.Add(CreateItemMonitorColorCard());
            return stack;
        }

        private Panel BuildInventoryTrendContent()
        {
            var stack = CreateStackPanel();
            stack.Controls.Add(CreateInventoryDisplayCard());
            stack.Controls.Add(CreateInventoryColorCard());
            stack.Controls.Add(CreateInventoryTextCard());
            return stack;
        }

        private RedesignCardPanel CreateFloatBehaviorCard()
        {
            var card = CreateCard("悬浮窗行为", "控制桌面浮层是否显示、是否可点击和是否自动隐藏", UIUtils.S(176));
            var grid = CreateThreeColumnGrid(card, top: 56, height: 104);
            grid.Controls.Add(CreateSwitchTile("开启悬浮窗", "启用", "保留桌面实时行情入口", BindSwitch(() => !Get(nameof(Settings.HideMainForm), false), value =>
            {
                Set(nameof(Settings.HideMainForm), !value);
                MainPanelSafeVisibilityResult visibility = EnsureSafeVisibility();
                ApplySafeVisibilityToRuntime(visibility);
                ReloadRuntimeDisplay();
            })), 0, 0);
            grid.Controls.Add(CreateSegmentTile("置顶 / 锁定", "防止误拖动", new[] { "窗口置顶", "锁定位置" },
                new[] { nameof(Settings.TopMost), nameof(Settings.LockPosition) }), 1, 0);
            grid.Controls.Add(CreateSegmentTile("安全显示", "避免超出屏幕边缘", new[] { "限制在屏幕内", "自动隐藏" },
                new[] { nameof(Settings.ClampToScreen), nameof(Settings.AutoHide) }, new[] { true, false }), 2, 0);
            return card;
        }

        private RedesignCardPanel CreateFloatAppearanceCard()
        {
            var card = CreateCard("外观与布局", "方向用分段，宽度 / 透明度 / 缩放用滑块", UIUtils.S(210));
            int innerGap = UIUtils.S(16);
            int innerLeft = UIUtils.S(18);
            int innerTop = UIUtils.S(58);
            int innerWidth = Math.Max(UIUtils.S(300), (card.Width - UIUtils.S(36) - innerGap) / 2);
            var left = CreateInnerCard();
            left.SetBounds(innerLeft, innerTop, innerWidth, UIUtils.S(136));
            card.Controls.Add(left);
            left.Controls.Add(CreateLabel("显示模式", 18, 14, 160, 24, UIFonts.Bold(9f), UIColors.TextSub));
            var mode = new RedesignSegmentedControl("竖向", "横向单行")
            {
                Width = UIUtils.S(240),
                Left = UIUtils.S(18),
                Top = UIUtils.S(48)
            };
            BindSegment(mode, () => Get(nameof(Settings.HorizontalMode), false) ? 1 : 0, index =>
            {
                bool horizontal = index == 1;
                Set(nameof(Settings.HorizontalMode), horizontal);
                Set(nameof(Settings.HorizontalSingleLine), horizontal);
                ReloadRuntimeDisplay();
            });
            left.Controls.Add(mode);
            AddSliderRow(left, "界面宽度", 18, 92, nameof(Settings.PanelWidth), 220, 180, 1200, "px", v => Math.Clamp(v, 180, 1200));

            var right = CreateInnerCard();
            right.SetBounds(innerLeft + innerWidth + innerGap, innerTop, innerWidth, UIUtils.S(136));
            card.Controls.Add(right);
            AddPercentSliderRow(right, "背景透明度", 18, 16, nameof(Settings.PanelBackgroundOpacity), invert: true);
            AddPercentSliderRow(right, "文字透明度", 18, 56, nameof(Settings.TextOpacity), invert: true, maxHiddenPercent: 70);
            AddColorButtonRow(right, "背景色", 18, 96, nameof(Settings.PanelBackgroundColor), "", "默认皮肤", _ => ReloadRuntimeDisplay());
            card.SizeChanged += (_, __) =>
            {
                int currentInnerWidth = Math.Max(UIUtils.S(300), (card.Width - UIUtils.S(36) - innerGap) / 2);
                left.SetBounds(innerLeft, innerTop, currentInnerWidth, UIUtils.S(136));
                right.SetBounds(innerLeft + currentInnerWidth + innerGap, innerTop, currentInnerWidth, UIUtils.S(136));
            };
            return card;
        }

        private RedesignCardPanel CreateFloatSpacingCard()
        {
            var card = CreateCard("间距", "横向模式固定单行显示，间距可为正负数", UIUtils.S(118));
            AddSliderRow(card, "项目间距", 28, 54, nameof(Settings.HorizontalItemSpacing), 12, -20, 80, "px", v => v);
            AddSliderRow(card, "数值间距", 350, 54, nameof(Settings.HorizontalInnerSpacing), 8, -20, 80, "px", v => v);
            return card;
        }

        private RedesignCardPanel CreateTaskbarDisplayCard()
        {
            var card = CreateCard("任务栏显示", "控制是否显示任务栏浮层，以及任务栏文字样式", UIUtils.S(176));
            var grid = CreateThreeColumnGrid(card, top: 56, height: 104);
            grid.Controls.Add(CreateSwitchTile("显示任务栏", "启用", "在 Windows 任务栏区域显示", BindSwitch(() => Get(nameof(Settings.ShowTaskbar), true), value =>
            {
                Set(nameof(Settings.ShowTaskbar), value);
                MainPanelSafeVisibilityResult visibility = EnsureSafeVisibility();
                ApplySafeVisibilityToRuntime(visibility);
                ReloadRuntimeDisplay();
            })), 0, 0);
            var preset = new RedesignSegmentedControl("粗体", "常规") { Width = UIUtils.S(196), Left = UIUtils.S(18), Top = UIUtils.S(42) };
            BindSegment(preset, () => Get(nameof(Settings.TaskbarPresetStyle), 1) == 1 ? 0 : 1, index =>
            {
                ApplyTaskbarStylePreset(index == 0);
            });
            grid.Controls.Add(CreateControlTile("任务栏样式", "粗体更醒目，常规更紧凑", preset), 1, 0);
            grid.Controls.Add(CreateSwitchTile("单行显示", Get(nameof(Settings.TaskbarSingleLine), false) ? "启用" : "关闭", "横向压缩为一行", BindSwitch(nameof(Settings.TaskbarSingleLine), false, _ => ReloadRuntimeDisplay())), 2, 0);
            return card;
        }

        private RedesignCardPanel CreateTaskbarAdvancedCard()
        {
            var card = CreateCard("任务栏高级", "鼠标悬停行为", UIUtils.S(116));
            card.Controls.Add(CreateHorizontalSwitchRow("鼠标悬停显示详情", "显示被折叠的完整监控项", 28, 56, nameof(Settings.TaskbarHoverShowAll), true));
            return card;
        }

        private RedesignCardPanel CreateTaskbarPositionCard()
        {
            var card = CreateCard("位置与偏移", "Windows 11 任务栏居中时，可设置左侧显示和左右偏移", UIUtils.S(176));
            var segment = new RedesignSegmentedControl("任务栏右侧", "任务栏左侧")
            {
                Left = UIUtils.S(130),
                Top = UIUtils.S(56),
                Width = UIUtils.S(260)
            };
            BindSegment(segment, () => Get(nameof(Settings.TaskbarAlignLeft), false) ? 1 : 0, index =>
            {
                Set(nameof(Settings.TaskbarAlignLeft), index == 1);
                ReloadRuntimeDisplay();
            });
            card.Controls.Add(CreateLabel("显示位置", 28, 58, 100, 26, UIFonts.Bold(9f), UIColors.TextMain));
            card.Controls.Add(segment);
            AddSliderRow(card, "左右偏移量", 28, 104, nameof(Settings.TaskbarManualOffset), 0, -1200, 1200, "px", v => Math.Clamp(v, -1200, 1200));
            return card;
        }

        private RedesignCardPanel CreateFontCard()
        {
            var card = CreateCard("字体", "任务栏和横向悬浮窗共用字体设置", UIUtils.S(214));
            AddFloatSliderRow(card, "字体大小", 28, 58, nameof(Settings.TaskbarFontSize), Settings.DEFAULT_TB_SIZE_BOLD, 7, 18, "pt");
            card.Controls.Add(CreateHorizontalSwitchRow("加粗", "任务栏文字加粗", 28, 102, nameof(Settings.TaskbarFontBold), true));

            var combo = new LiteComboBox();
            combo.Items.Add("Microsoft YaHei UI");
            combo.Items.Add("Segoe UI");
            combo.Items.Add("Arial");
            combo.Items.Add(Get(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT));
            combo.Width = UIUtils.S(230);
            combo.Left = UIUtils.S(130);
            combo.Top = UIUtils.S(154);
            SelectCombo(combo, Get(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT));
            combo.Inner.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls) return;
                Set(nameof(Settings.TaskbarFontFamily), combo.Text);
                ReloadRuntimeDisplay();
            };
            RegisterRefresh(() => SelectCombo(combo, Get(nameof(Settings.TaskbarFontFamily), Settings.DEFAULT_TB_FONT)));
            RegisterSave(() => Set(nameof(Settings.TaskbarFontFamily), combo.Text));
            card.Controls.Add(CreateLabel("字体", 28, 156, 100, 26, UIFonts.Bold(9f), UIColors.TextMain));
            card.Controls.Add(combo);
            return card;
        }

        private RedesignCardPanel CreateStyleSpacingCard()
        {
            var card = CreateCard("间距", "这里是悬浮窗横向显示和任务栏共用的间距设置", UIUtils.S(222));
            AddSliderRow(card, "项目间距", 28, 58, nameof(Settings.TaskbarItemSpacing), Settings.DEFAULT_TB_GAP, -20, 80, "px", v => v);
            AddSliderRow(card, "数值间距", 350, 58, nameof(Settings.TaskbarInnerSpacing), Settings.DEFAULT_TB_INNER_BOLD, -20, 80, "px", v => v);
            AddSliderRow(card, "上下边距", 28, 104, nameof(Settings.TaskbarVerticalPadding), Settings.DEFAULT_TB_VOFF, -10, 30, "px", v => v);
            var presets = new RedesignSegmentedControl("默认", "紧凑", "高对比", "电竞")
            {
                Left = UIUtils.S(130),
                Top = UIUtils.S(154),
                Width = UIUtils.S(300)
            };
            BindSegment(presets, ResolveStylePresetSelection, index =>
            {
                _stylePresetSelection = index;
                ApplyPreset(index);
            });
            card.Controls.Add(CreateLabel("预设", 28, 157, 100, 24, UIFonts.Bold(9f), UIColors.TextMain));
            card.Controls.Add(presets);
            return card;
        }

        private RedesignCardPanel CreateGeneralColorCard()
        {
            var card = CreateCard("颜色", "任务栏和横向悬浮窗共用颜色规则", UIUtils.S(204));
            AddColorTile(card, "背景色", nameof(Settings.TaskbarColorBg), "#001E3D", 28, 58);
            AddColorTile(card, "标签颜色", nameof(Settings.TaskbarColorLabel), "#FFFFFF", 196, 58);
            AddColorTile(card, "上涨颜色", nameof(Settings.TaskbarColorCrit), "#FF4444", 364, 58);
            AddColorTile(card, "下跌颜色", nameof(Settings.TaskbarColorSafe), "#00CC66", 532, 58);
            AddColorTile(card, "异常/过期颜色", nameof(Settings.TaskbarColorWarn), "#FFFF00", 28, 124);
            return card;
        }

        private RedesignCardPanel CreateItemMonitorListCard()
        {
            var card = CreateCard("单品监控", "这里只设置全局默认规则和已监控单品的显示方式，不负责添加新饰品", UIUtils.S(372));
            List<ItemMonitorConfig> items = GetList<ItemMonitorConfig>(nameof(Settings.ItemConfigs));
            ItemMonitorConfig? CurrentItem() => MainPanelSettingsPageModel.GetPrimaryItem(items);

            card.Controls.Add(CreateLabel("全局设置统一控制抓取和百分比提醒；单品行只填写需要覆盖的价格或涨跌条件。", 18, 52, 760, 24, UIFonts.Regular(8.8f), UIColors.TextSub));

            AddCompactValueRow(card, "全局抓取间隔", 28, 88, nameof(Settings.DefaultItemRefreshIntervalSec), "600", "秒");
            AddCompactValueRow(card, "默认上涨提醒", 420, 88, nameof(Settings.DefaultItemPriceAlertRisePercent), "0", "%");
            AddCompactValueRow(card, "默认下跌提醒", 28, 126, nameof(Settings.DefaultItemPriceAlertFallPercent), "0", "%");
            AddCompactValueRow(card, "默认统计窗口", 420, 126, nameof(Settings.DefaultItemPriceAlertWindowMinutes), "10", "分");
            AddCompactValueRow(card, "默认提醒冷却", 28, 164, nameof(Settings.DefaultItemPriceAlertCooldownMinutes), "10", "分");
            card.Controls.Add(CreateLabel("默认上涨/下跌填 0 表示不启用全局百分比提醒；单品行里的覆盖值优先生效。", 18, 202, 720, 22, UIFonts.Regular(8.5f), UIColors.TextSub));

            card.Controls.Add(CreateDivider(18, 226, card.Width - UIUtils.S(36)));
            var titleLabel = CreateLabel(MainPanelSettingsPageModel.BuildItemTitle(CurrentItem()), 18, 240, 300, 26, UIFonts.Bold(10f), UIColors.TextMain);
            var statusLabel = CreateLabel(MainPanelSettingsPageModel.BuildItemStatus(CurrentItem()), 18, 264, 330, 22, UIFonts.Regular(8.5f), MainPanelSettingsPageModel.ResolveItemStatusColor(CurrentItem()));
            card.Controls.Add(titleLabel);
            card.Controls.Add(statusLabel);

            var refresh = new LiteButton("刷新", false) { Width = UIUtils.S(72), Height = UIUtils.S(30) };
            refresh.SetBounds(card.Width - UIUtils.S(160), UIUtils.S(242), UIUtils.S(72), UIUtils.S(30));
            refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var delete = new LiteButton("删除", false) { Width = UIUtils.S(72), Height = UIUtils.S(30) };
            delete.SetBounds(card.Width - UIUtils.S(80), UIUtils.S(242), UIUtils.S(72), UIUtils.S(30));
            delete.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            refresh.Click += async (_, __) => await RefreshCurrentItemAsync(items, CurrentItem(), refresh);
            delete.Click += (_, __) => DeleteCurrentItem(items, CurrentItem());
            RegisterRefresh(() =>
            {
                ItemMonitorConfig? item = CurrentItem();
                titleLabel.Text = MainPanelSettingsPageModel.BuildItemTitle(item);
                statusLabel.Text = MainPanelSettingsPageModel.BuildItemStatus(item);
                statusLabel.ForeColor = MainPanelSettingsPageModel.ResolveItemStatusColor(item);
                refresh.Enabled = item is not null && !_refreshingItemPrice;
                delete.Enabled = item is not null;
            });
            card.Controls.Add(refresh);
            card.Controls.Add(delete);

            card.Controls.Add(CreateLabel("短名称", 18, 298, 70, 24, UIFonts.Bold(9f), UIColors.TextMain));
            var shortName = new LiteUnderlineInput(CurrentItem()?.ShortName ?? "", "", "", 160)
            {
                Left = UIUtils.S(92),
                Top = UIUtils.S(292)
            };
            shortName.Placeholder = "短名称";
            shortName.Inner.TextChanged += (_, __) =>
            {
                if (IsUpdatingControls)
                    return;

                ItemMonitorConfig? item = CurrentItem();
                if (item is null)
                    return;

                item.ShortName = shortName.Inner.Text.Trim();
                CommitItemConfigs(items);
            };
            RegisterRefresh(() =>
            {
                ItemMonitorConfig? item = CurrentItem();
                shortName.Enabled = item is not null;
                shortName.Inner.Text = item?.ShortName ?? "";
            });
            card.Controls.Add(shortName);

            card.Controls.Add(CreateLabel("显示位置", 320, 298, 80, 24, UIFonts.Bold(9f), UIColors.TextMain));
            card.Controls.Add(CreateItemLabeledSwitch("启用", 400, 295, CurrentItem, item => item.Enabled, (item, value) => item.Enabled = value, items));
            card.Controls.Add(CreateItemLabeledSwitch("悬浮窗", 500, 295, CurrentItem, item => item.VisibleInPanel, (item, value) => item.VisibleInPanel = value, items));
            card.Controls.Add(CreateItemLabeledSwitch("任务栏", 620, 295, CurrentItem, item => item.VisibleInTaskbar, (item, value) => item.VisibleInTaskbar = value, items));

            card.Controls.Add(CreateLabel("显示字段", 18, 334, 70, 24, UIFonts.Bold(9f), UIColors.TextMain));
            var fields = CreateItemFieldStrip(CurrentItem, items);
            fields.SetBounds(UIUtils.S(92), UIUtils.S(330), Math.Max(UIUtils.S(440), card.Width - UIUtils.S(220)), UIUtils.S(30));
            fields.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Controls.Add(fields);
            return card;
        }

        private RedesignCardPanel CreateItemMonitorColorCard()
        {
            var card = CreateCard("单品颜色", "单品价格、涨跌颜色与大盘 / 任务栏显示管线共用", UIUtils.S(156));
            AddColorTile(card, "上涨颜色", nameof(Settings.SteamDtPositiveColor), "#FF4444", 28, 58);
            AddColorTile(card, "下跌颜色", nameof(Settings.SteamDtNegativeColor), "#00CC66", 196, 58);
            AddColorTile(card, "异常/过期颜色", nameof(Settings.SteamDtWarningColor), "#FFFF00", 364, 58);
            AddColorTile(card, "普通文字颜色", nameof(Settings.SteamDtNeutralColor), "#FFFFFF", 532, 58);
            return card;
        }

        private RedesignCardPanel CreateInventoryDisplayCard()
        {
            var card = CreateCard("库存涨跌显示", "把今日盈亏作为一个指标组件，控制显示位置和展示样式", UIUtils.S(198));
            int innerGap = UIUtils.S(16);
            int innerLeft = UIUtils.S(18);
            int innerTop = UIUtils.S(58);
            int innerWidth = Math.Max(UIUtils.S(300), (card.Width - UIUtils.S(36) - innerGap) / 2);
            var preview = CreateInnerCard();
            preview.SetBounds(innerLeft, innerTop, innerWidth, UIUtils.S(122));
            card.Controls.Add(preview);
            preview.Controls.Add(CreateLabel("指标预览", 18, 14, 160, 24, UIFonts.Regular(9f), UIColors.TextSub));
            var previewValue = CreateLabel(BuildInventoryPreviewText(), 18, 42, 300, 34, UIFonts.Bold(16f), ParseColor(Get(nameof(Settings.YouPinTrendIndicatorProfitColor), "#DC465A"), UIColors.Negative));
            preview.Controls.Add(previewValue);
            RegisterRefresh(() => previewValue.Text = BuildInventoryPreviewText());
            preview.Controls.Add(CreateLabel("库存今日盈亏", 18, 78, 160, 24, UIFonts.Regular(9f), UIColors.TextSub));
            preview.Controls.Add(CreateLabeledSwitch("悬浮窗", 190, 78, nameof(Settings.YouPinTrendIndicatorVisibleInPanel), true, _ => ReloadRuntimeDisplay()));
            preview.Controls.Add(CreateLabeledSwitch("任务栏", 300, 78, nameof(Settings.YouPinTrendIndicatorVisibleInTaskbar), true, _ => ReloadRuntimeDisplay()));

            var style = CreateInnerCard();
            style.SetBounds(innerLeft + innerWidth + innerGap, innerTop, innerWidth, UIUtils.S(122));
            card.Controls.Add(style);
            style.Controls.Add(CreateLabel("展示样式", 18, 14, 160, 24, UIFonts.Regular(9f), UIColors.TextSub));
            var segment = new RedesignSegmentedControl("金额 + 百分比", "仅金额", "仅百分比")
            {
                Left = UIUtils.S(18),
                Top = UIUtils.S(44),
                Width = Math.Max(UIUtils.S(260), innerWidth - UIUtils.S(48))
            };
            BindSegment(segment, () => Get(nameof(Settings.YouPinTrendIndicatorDisplayMode), 0), index =>
            {
                Set(nameof(Settings.YouPinTrendIndicatorDisplayMode), index);
                previewValue.Text = BuildInventoryPreviewText();
                ReloadRuntimeDisplay();
            });
            style.Controls.Add(segment);
            style.Controls.Add(CreateLabel("控制库存今日盈亏在悬浮窗和任务栏里的展示内容。", 18, 82, 320, 24, UIFonts.Regular(8.5f), UIColors.TextSub));
            card.SizeChanged += (_, __) =>
            {
                int currentInnerWidth = Math.Max(UIUtils.S(300), (card.Width - UIUtils.S(36) - innerGap) / 2);
                preview.SetBounds(innerLeft, innerTop, currentInnerWidth, UIUtils.S(122));
                style.SetBounds(innerLeft + currentInnerWidth + innerGap, innerTop, currentInnerWidth, UIUtils.S(122));
                segment.Width = Math.Max(UIUtils.S(260), currentInnerWidth - UIUtils.S(48));
            };
            return card;
        }

        private RedesignCardPanel CreateInventoryColorCard()
        {
            var card = CreateCard("颜色规则", "同一组指标共用颜色规则", UIUtils.S(156));
            AddColorTile(card, "盈利颜色", nameof(Settings.YouPinTrendIndicatorProfitColor), "#DC465A", 28, 58);
            AddColorTile(card, "亏损颜色", nameof(Settings.YouPinTrendIndicatorLossColor), "#50A087", 196, 58);
            AddColorTile(card, "零值颜色", nameof(Settings.YouPinTrendIndicatorZeroColor), "#FFFFFF", 364, 58);
            AddColorTile(card, "辅助文字", nameof(Settings.YouPinTrendIndicatorSubTextColor), "#8D9BAB", 532, 58);
            return card;
        }

        private RedesignCardPanel CreateInventoryTextCard()
        {
            var card = CreateCard("文字样式", "只影响悬浮窗和任务栏里的库存涨跌指标；库存涨跌页面本身不受影响。", UIUtils.S(176));
            AddFloatSliderRow(card, "数字字号", 28, 58, nameof(Settings.YouPinTrendIndicatorFontSize), 9f, 7, 18, "pt");
            card.Controls.Add(CreateHorizontalSwitchRow("数字加粗", "启用", 350, 62, nameof(Settings.YouPinTrendIndicatorFontBold), true));
            var signSegment = new RedesignSegmentedControl("带 +/-", "不带符号")
            {
                Left = UIUtils.S(130),
                Top = UIUtils.S(112),
                Width = UIUtils.S(240)
            };
            BindSegment(signSegment, () => Get(nameof(Settings.YouPinTrendIndicatorSignMode), 0), index =>
            {
                Set(nameof(Settings.YouPinTrendIndicatorSignMode), index);
                RefreshFromStore();
                ReloadRuntimeDisplay();
            });
            card.Controls.Add(CreateLabel("符号格式", 28, 114, 100, 26, UIFonts.Bold(9f), UIColors.TextMain));
            card.Controls.Add(signSegment);
            return card;
        }

        private string BuildInventoryPreviewText()
        {
            return MainPanelSettingsPageModel.BuildInventoryPreviewText(
                Get(nameof(Settings.YouPinTrendIndicatorDisplayMode), 0),
                Get(nameof(Settings.YouPinTrendIndicatorSignMode), 0));
        }

        private Panel CreateStackPanel()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
        }

        private RedesignCardPanel CreateCard(string title, string subtitle, int height)
        {
            int width = ContentWidth;
            var card = new RedesignCardPanel(UIColors.CardBg)
            {
                Width = width,
                Height = height,
                Margin = new Padding(0, 0, 0, UIUtils.S(14)),
                Padding = UIUtils.S(new Padding(18))
            };
            var titleLabel = CreateLabel(title, 18, 16, 260, 28, UIFonts.Bold(10.5f), UIColors.TextMain);
            card.Controls.Add(titleLabel);
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                var subtitleLabel = new Label
                {
                    Text = subtitle,
                    AutoSize = false,
                    Left = UIUtils.S(300),
                    Top = UIUtils.S(20),
                    Width = Math.Max(UIUtils.S(220), width - UIUtils.S(318)),
                    Height = UIUtils.S(22),
                    Font = UIFonts.Regular(8.5f),
                    ForeColor = UIColors.TextSub,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleRight,
                    AutoEllipsis = true
                };
                card.Controls.Add(subtitleLabel);
                card.Layout += (_, __) =>
                {
                    int left = Math.Min(
                        Math.Max(titleLabel.Right + UIUtils.S(12), UIUtils.S(300)),
                        Math.Max(UIUtils.S(18), card.Width - UIUtils.S(240)));
                    subtitleLabel.SetBounds(
                        left,
                        UIUtils.S(20),
                        Math.Max(UIUtils.S(120), card.Width - left - UIUtils.S(18)),
                        UIUtils.S(22));
                };
            }
            return card;
        }

        private TableLayoutPanel CreateThreeColumnGrid(Control parent, int top, int height)
        {
            var grid = new TableLayoutPanel
            {
                Left = UIUtils.S(18),
                Top = UIUtils.S(top),
                Width = Math.Max(1, parent.Width - UIUtils.S(36)),
                Height = UIUtils.S(height),
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            parent.Controls.Add(grid);
            return grid;
        }

        private RedesignCardPanel CreateInnerCard()
        {
            return new RedesignCardPanel(UIColors.InputBg, radius: 6)
            {
                Padding = UIUtils.S(new Padding(18))
            };
        }

        private Control CreateSwitchTile(string title, string state, string subtitle, RedesignSwitch toggle)
        {
            var tile = CreateInnerCard();
            tile.Dock = DockStyle.Fill;
            tile.Margin = UIUtils.S(new Padding(0, 0, 12, 0));
            tile.Controls.Add(CreateLabel(title, 18, 14, 180, 24, UIFonts.Regular(9f), UIColors.TextSub));
            var stateLabel = CreateLabel(toggle.Checked ? "启用" : "关闭", 82, 44, 80, 26, UIFonts.Bold(10f), UIColors.TextMain);
            tile.Controls.Add(stateLabel);
            tile.Controls.Add(CreateLabel(subtitle, 18, 66, 240, 22, UIFonts.Regular(8f), UIColors.TextSub));
            toggle.Left = UIUtils.S(18);
            toggle.Top = UIUtils.S(42);
            tile.Controls.Add(toggle);
            toggle.CheckedChanged += (_, __) => stateLabel.Text = toggle.Checked ? "启用" : "关闭";
            return tile;
        }

        private Control CreateStatTile(string title, string value, string subtitle, RedesignSwitch toggle)
        {
            var tile = CreateInnerCard();
            tile.Dock = DockStyle.Fill;
            tile.Margin = UIUtils.S(new Padding(0, 0, 12, 0));
            tile.Controls.Add(CreateLabel(title, 18, 14, 190, 24, UIFonts.Regular(9f), UIColors.TextSub));
            var valueLabel = CreateLabel(value, 18, 42, 160, 34, UIFonts.Bold(14f), UIColors.TextMain);
            tile.Controls.Add(valueLabel);
            tile.Controls.Add(CreateLabel(subtitle, 18, 76, 260, 22, UIFonts.Regular(8f), UIColors.TextSub));
            toggle.Left = UIUtils.S(238);
            toggle.Top = UIUtils.S(18);
            tile.Controls.Add(toggle);
            toggle.CheckedChanged += (_, __) => valueLabel.Text = toggle.Checked ? "启用" : "关闭";
            return tile;
        }

        private Control CreateStaticHintTile(string title, string subtitle)
        {
            var tile = CreateInnerCard();
            tile.Dock = DockStyle.Fill;
            tile.Margin = Padding.Empty;
            tile.Controls.Add(CreateLabel(title, 18, 18, 200, 26, UIFonts.Bold(10f), UIColors.TextMain));
            tile.Controls.Add(CreateLabel(subtitle, 18, 50, 260, 48, UIFonts.Regular(8.5f), UIColors.TextSub));
            return tile;
        }

        private Control CreateControlTile(string title, string subtitle, Control control)
        {
            var tile = CreateInnerCard();
            tile.Dock = DockStyle.Fill;
            tile.Margin = UIUtils.S(new Padding(0, 0, 12, 0));
            tile.Controls.Add(CreateLabel(title, 18, 14, 180, 24, UIFonts.Regular(9f), UIColors.TextSub));
            tile.Controls.Add(control);
            tile.Controls.Add(CreateLabel(subtitle, 18, 74, 240, 22, UIFonts.Regular(8f), UIColors.TextSub));
            return tile;
        }

        private Control CreateSegmentTile(string title, string subtitle, string[] texts, string[] settingKeys, bool[]? fallbacks = null)
        {
            var tile = CreateInnerCard();
            tile.Dock = DockStyle.Fill;
            tile.Margin = UIUtils.S(new Padding(0, 0, 12, 0));
            tile.Controls.Add(CreateLabel(title, 18, 14, 180, 24, UIFonts.Regular(9f), UIColors.TextSub));

            var options = new FlowLayoutPanel
            {
                Left = UIUtils.S(18),
                Top = UIUtils.S(42),
                Width = UIUtils.S(260),
                Height = UIUtils.S(28),
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            tile.Controls.Add(options);

            for (int i = 0; i < texts.Length; i++)
            {
                bool fallback = fallbacks is not null && i < fallbacks.Length && fallbacks[i];
                var sw = BindSwitch(settingKeys[i], fallback, _ => ReloadRuntimeDisplay());
                options.Controls.Add(CreateSegmentSwitchOption(texts[i], sw, i == texts.Length - 1));
            }
            tile.Controls.Add(CreateLabel(subtitle, 18, 72, 240, 22, UIFonts.Regular(8f), UIColors.TextSub));
            return tile;
        }

        private static Control CreateSegmentSwitchOption(string text, RedesignSwitch toggle, bool isLast)
        {
            int minimumLabelWidth = UIUtils.S(text.Length >= 6 ? 92 : 64);
            int labelWidth = Math.Max(
                minimumLabelWidth,
                TextRenderer.MeasureText(text, UIFonts.Bold(9f), Size.Empty, TextFormatFlags.NoPadding).Width + UIUtils.S(12));
            var option = new Panel
            {
                Width = toggle.Width + UIUtils.S(6) + labelWidth,
                Height = UIUtils.S(28),
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, isLast ? 0 : UIUtils.S(8), 0),
                Padding = Padding.Empty
            };

            toggle.Left = 0;
            toggle.Top = UIUtils.S(2);
            option.Controls.Add(toggle);

            option.Controls.Add(new Label
            {
                Text = text,
                AutoSize = false,
                Left = toggle.Right + UIUtils.S(6),
                Top = UIUtils.S(1),
                Width = labelWidth,
                Height = UIUtils.S(24),
                Font = UIFonts.Bold(9f),
                ForeColor = UIColors.TextMain,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            });

            return option;
        }

        private Control CreateHorizontalSwitchRow(string title, string subtitle, int x, int y, string settingKey, bool fallback, Action<bool>? afterChanged = null)
        {
            int panelWidth = Math.Max(UIUtils.S(260), ContentWidth - UIUtils.S(x + 36));
            var panel = new Panel
            {
                Left = UIUtils.S(x),
                Top = UIUtils.S(y),
                Width = panelWidth,
                Height = UIUtils.S(44),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            panel.Controls.Add(CreateLabel(title, 0, 0, 170, 24, UIFonts.Bold(9f), UIColors.TextMain));
            panel.Controls.Add(CreateLabel(subtitle, 0, 22, 240, 20, UIFonts.Regular(8f), UIColors.TextSub));
            var toggle = BindSwitch(settingKey, fallback, value =>
            {
                afterChanged?.Invoke(value);
                ReloadRuntimeDisplay();
            });
            toggle.Left = Math.Max(UIUtils.S(180), panelWidth - UIUtils.S(62));
            toggle.Top = UIUtils.S(4);
            toggle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            panel.Controls.Add(toggle);
            return panel;
        }

        private Control CreateLabeledSwitch(string text, int x, int y, string settingKey, bool fallback, Action<bool>? afterChanged)
        {
            var panel = new Panel
            {
                Left = UIUtils.S(x),
                Top = UIUtils.S(y),
                Width = UIUtils.S(108),
                Height = UIUtils.S(28),
                BackColor = Color.Transparent
            };
            var sw = BindSwitch(settingKey, fallback, afterChanged);
            sw.Left = 0;
            sw.Top = UIUtils.S(2);
            panel.Controls.Add(sw);
            panel.Controls.Add(CreateLabel(text, 50, 0, 58, 26, UIFonts.Bold(9f), UIColors.TextMain));
            return panel;
        }

        private Control CreateItemLabeledSwitch(
            string text,
            int x,
            int y,
            Func<ItemMonitorConfig?> getItem,
            Func<ItemMonitorConfig, bool> get,
            Action<ItemMonitorConfig, bool> set,
            List<ItemMonitorConfig> items)
        {
            var panel = new Panel
            {
                Left = UIUtils.S(x),
                Top = UIUtils.S(y),
                Width = UIUtils.S(108),
                Height = UIUtils.S(28),
                BackColor = Color.Transparent
            };
            var sw = BindSwitch(
                () => getItem() is { } item && get(item),
                value =>
                {
                    ItemMonitorConfig? item = getItem();
                    if (item is null)
                        return;

                    set(item, value);
                    CommitItemConfigs(items);
                });
            sw.Left = 0;
            sw.Top = UIUtils.S(2);
            RegisterRefresh(() => sw.Enabled = getItem() is not null);
            panel.Controls.Add(sw);
            panel.Controls.Add(CreateLabel(text, 50, 0, 58, 26, UIFonts.Bold(9f), UIColors.TextMain));
            return panel;
        }

        private FlowLayoutPanel CreateItemFieldStrip(Func<ItemMonitorConfig?> getItem, List<ItemMonitorConfig> items)
        {
            var panel = new FlowLayoutPanel
            {
                AutoSize = false,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            foreach (ItemMonitorDisplayFieldOption option in ItemMonitorDisplayFields.Options)
            {
                var item = new Panel
                {
                    Width = UIUtils.S(option.Text.Length >= 5 ? 100 : 72),
                    Height = UIUtils.S(28),
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 0, UIUtils.S(6), 0),
                    Cursor = Cursors.Hand
                };
                var box = new Panel
                {
                    Left = 0,
                    Top = UIUtils.S(5),
                    Width = UIUtils.S(16),
                    Height = UIUtils.S(16),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                box.Paint += (_, e) =>
                {
                    bool isChecked = MainPanelSettingsPageModel.IsItemFieldEnabled(getItem(), option.Flag);
                    using var pen = new Pen(isChecked ? UIColors.Primary : UIColors.Border, 1);
                    e.Graphics.DrawRectangle(pen, 0, 0, box.Width - 1, box.Height - 1);
                    if (isChecked)
                    {
                        using var checkPen = new Pen(Color.White, 2);
                        e.Graphics.DrawLines(checkPen, new[]
                        {
                            new Point(UIUtils.S(3), UIUtils.S(8)),
                            new Point(UIUtils.S(7), UIUtils.S(12)),
                            new Point(UIUtils.S(13), UIUtils.S(4))
                        });
                    }
                };
                item.Controls.Add(box);
                var label = CreateLabel(option.Text, 22, 2, option.Text.Length >= 5 ? 76 : 48, 24, UIFonts.Regular(8.8f), UIColors.TextMain);
                label.Cursor = Cursors.Hand;
                item.Controls.Add(label);
                item.Click += (_, __) => ToggleItemField(getItem(), option.Flag, items, box);
                box.Click += (_, __) => ToggleItemField(getItem(), option.Flag, items, box);
                label.Click += (_, __) => ToggleItemField(getItem(), option.Flag, items, box);
                RegisterRefresh(() =>
                {
                    bool hasItem = getItem() is not null;
                    item.Enabled = hasItem;
                    label.ForeColor = hasItem ? UIColors.TextMain : UIColors.TextDisabled;
                    box.Invalidate();
                });
                panel.Controls.Add(item);
            }

            return panel;
        }

        private void ToggleItemField(ItemMonitorConfig? item, int flag, List<ItemMonitorConfig> items, Control invalidateTarget)
        {
            if (item is null)
                return;

            item.DisplayFieldFlags = MainPanelSettingsPageModel.ToggleItemFieldFlags(item.DisplayFieldFlags, flag);
            CommitItemConfigs(items);
            invalidateTarget.Invalidate();
        }

        private void CommitItemConfigs(List<ItemMonitorConfig> items)
        {
            Set(nameof(Settings.ItemConfigs), items);
            ReloadRuntimeDisplay();
        }

        private async Task RefreshCurrentItemAsync(List<ItemMonitorConfig> items, ItemMonitorConfig? item, Control sourceButton)
        {
            if (item is null || _refreshingItemPrice)
                return;

            _refreshingItemPrice = true;
            sourceButton.Enabled = false;
            try
            {
                _steamDtItemService.Configure(Get(nameof(Settings.SteamDtApiKey), string.Empty));
                await _steamDtItemService.FetchItemPriceAsync(item, persistSettings: false);
                if (PageToken.IsCancellationRequested || IsDisposed)
                    return;

                CommitItemConfigs(items);
                RefreshFromStore();
            }
            catch (Exception ex)
            {
                if (PageToken.IsCancellationRequested || IsDisposed)
                    return;

                item.LastStatus = "刷新失败：" + CS2TradeMonitor.src.Core.Actions.AppActions.SanitizeError(ex.Message);
                CommitItemConfigs(items);
                RefreshFromStore();
            }
            finally
            {
                _refreshingItemPrice = false;
                if (!sourceButton.IsDisposed)
                    sourceButton.Enabled = item is not null;
            }
        }

        private void DeleteCurrentItem(List<ItemMonitorConfig> items, ItemMonitorConfig? item)
        {
            if (item is null)
                return;

            string title = MainPanelSettingsPageModel.BuildItemTitle(item);
            DialogResult result = GlobalPromptService.Show(
                FindForm(),
                $"确定从单品监控中删除“{title}”吗？",
                "删除单品监控",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (result != DialogResult.OK)
                return;

            items.Remove(item);
            CommitItemConfigs(items);
            RefreshFromStore();
        }

        private void AddSliderRow(Control parent, string title, int x, int y, string key, int fallback, int min, int max, string unit, Func<int, int> normalize)
        {
            parent.Controls.Add(CreateLabel(title, x, y + 2, 110, 24, UIFonts.Bold(9f), UIColors.TextMain));
            var slider = new RedesignSlider
            {
                Left = UIUtils.S(x + 120),
                Top = UIUtils.S(y),
                Minimum = min,
                Maximum = max,
                Value = normalize(Get(key, fallback))
            };
            slider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var value = CreateValueBox(normalize(Get(key, fallback)).ToString(CultureInfo.InvariantCulture), unit);
            value.Left = Math.Max(slider.Left + UIUtils.S(96), parent.Width - UIUtils.S(18) - value.Width);
            value.Top = UIUtils.S(y - 2);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            slider.Width = Math.Max(UIUtils.S(88), value.Left - slider.Left - UIUtils.S(14));
            slider.ValueChanged += (_, __) =>
            {
                if (IsUpdatingControls) return;
                int normalized = normalize(slider.Value);
                Set(key, normalized);
                value.Text = normalized.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
                ReloadRuntimeDisplay();
            };
            RegisterRefresh(() =>
            {
                int normalized = normalize(Get(key, fallback));
                slider.Value = normalized;
                value.Text = normalized.ToString(CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);
            });
            RegisterSave(() => Set(key, normalize(slider.Value)));
            parent.Controls.Add(slider);
            parent.Controls.Add(value);
        }

        private void AddCompactValueRow(Control parent, string title, int x, int y, string key, string fallback, string unit)
        {
            parent.Controls.Add(CreateLabel(title, x, y + 2, 110, 24, UIFonts.Bold(9f), UIColors.TextMain));
            var input = new LiteUnderlineInput(ReadSettingText(key, fallback), unit, "", 88, null, HorizontalAlignment.Center)
            {
                Left = UIUtils.S(x + 120),
                Top = UIUtils.S(y - 2)
            };
            RegisterRefresh(() => input.Inner.Text = ReadSettingText(key, fallback));
            RegisterSave(() => SaveCompactValue(key, input.Inner.Text, fallback));
            input.Inner.Leave += (_, __) =>
            {
                SaveCompactValue(key, input.Inner.Text, fallback);
                input.Inner.Text = ReadSettingText(key, fallback);
                ReloadRuntimeDisplay();
            };
            parent.Controls.Add(input);
        }

        private string ReadSettingText(string key, string fallback)
        {
            return key switch
            {
                nameof(Settings.DefaultItemPriceAlertRisePercent) or nameof(Settings.DefaultItemPriceAlertFallPercent) =>
                    Get(key, double.TryParse(fallback, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleFallback) ? doubleFallback : 0d).ToString("0.#", CultureInfo.InvariantCulture),
                _ => Get(key, int.TryParse(fallback, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intFallback) ? intFallback : 0).ToString(CultureInfo.InvariantCulture)
            };
        }

        private void SaveCompactValue(string key, string text, string fallback)
        {
            object normalized = MainPanelSettingsPageModel.NormalizeCompactValue(key, text, fallback);
            Set(key, normalized);
            if (key == nameof(Settings.DefaultItemRefreshIntervalSec) && normalized is int intValue)
            {
                List<ItemMonitorConfig> items = GetList<ItemMonitorConfig>(nameof(Settings.ItemConfigs));
                foreach (ItemMonitorConfig item in items)
                    item.RefreshIntervalSec = intValue;
                Set(nameof(Settings.ItemConfigs), items);
            }
        }

        private void AddFloatSliderRow(Control parent, string title, int x, int y, string key, float fallback, int min, int max, string unit)
        {
            parent.Controls.Add(CreateLabel(title, x, y + 2, 110, 24, UIFonts.Bold(9f), UIColors.TextMain));
            int scaled = (int)Math.Round(Get(key, fallback) * 10);
            var slider = new RedesignSlider
            {
                Left = UIUtils.S(x + 120),
                Top = UIUtils.S(y),
                Minimum = min * 10,
                Maximum = max * 10,
                Value = Math.Clamp(scaled, min * 10, max * 10)
            };
            slider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var value = CreateValueBox((slider.Value / 10f).ToString("0.#", CultureInfo.InvariantCulture), unit);
            value.Left = Math.Max(slider.Left + UIUtils.S(96), parent.Width - UIUtils.S(18) - value.Width);
            value.Top = UIUtils.S(y - 2);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            slider.Width = Math.Max(UIUtils.S(88), value.Left - slider.Left - UIUtils.S(14));
            slider.ValueChanged += (_, __) =>
            {
                if (IsUpdatingControls) return;
                float normalized = Math.Clamp(slider.Value / 10f, min, max);
                Set(key, normalized);
                value.Text = normalized.ToString("0.#", CultureInfo.InvariantCulture) + " " + unit;
                ReloadRuntimeDisplay();
            };
            RegisterRefresh(() =>
            {
                float current = Math.Clamp(Get(key, fallback), min, max);
                slider.Value = (int)Math.Round(current * 10);
                value.Text = current.ToString("0.#", CultureInfo.InvariantCulture) + " " + unit;
            });
            RegisterSave(() => Set(key, Math.Clamp(slider.Value / 10f, min, max)));
            parent.Controls.Add(slider);
            parent.Controls.Add(value);
        }

        private void AddDoubleSliderRow(Control parent, string title, int x, int y, string key, double fallback, int min, int max, string unit)
        {
            parent.Controls.Add(CreateLabel(title, x, y + 2, 110, 24, UIFonts.Bold(9f), UIColors.TextMain));
            int scaled = (int)Math.Round(Get(key, fallback) * 10);
            var slider = new RedesignSlider
            {
                Left = UIUtils.S(x + 120),
                Top = UIUtils.S(y),
                Minimum = min * 10,
                Maximum = max * 10,
                Value = Math.Clamp(scaled, min * 10, max * 10)
            };
            slider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var value = CreateValueBox((slider.Value / 10d).ToString("0.#", CultureInfo.InvariantCulture), unit);
            value.Left = Math.Max(slider.Left + UIUtils.S(96), parent.Width - UIUtils.S(18) - value.Width);
            value.Top = UIUtils.S(y - 2);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            slider.Width = Math.Max(UIUtils.S(88), value.Left - slider.Left - UIUtils.S(14));
            slider.ValueChanged += (_, __) =>
            {
                if (IsUpdatingControls) return;
                double normalized = Math.Clamp(slider.Value / 10d, min, max);
                Set(key, normalized);
                value.Text = normalized.ToString("0.#", CultureInfo.InvariantCulture) + " " + unit;
                ReloadRuntimeDisplay();
            };
            RegisterRefresh(() =>
            {
                double current = Math.Clamp(Get(key, fallback), min, max);
                slider.Value = (int)Math.Round(current * 10);
                value.Text = current.ToString("0.#", CultureInfo.InvariantCulture) + " " + unit;
            });
            RegisterSave(() => Set(key, Math.Clamp(slider.Value / 10d, min, max)));
            parent.Controls.Add(slider);
            parent.Controls.Add(value);
        }

        private void AddPercentSliderRow(Control parent, string title, int x, int y, string key, bool invert, int maxHiddenPercent = 100)
        {
            double stored = Get(key, 1.0);
            int percent = invert ? (int)Math.Round((1.0 - stored) * 100) : (int)Math.Round(stored * 100);
            parent.Controls.Add(CreateLabel(title, x, y + 2, 110, 24, UIFonts.Bold(9f), UIColors.TextMain));
            var slider = new RedesignSlider
            {
                Left = UIUtils.S(x + 120),
                Top = UIUtils.S(y),
                Minimum = 0,
                Maximum = maxHiddenPercent,
                Value = Math.Clamp(percent, 0, maxHiddenPercent)
            };
            slider.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            var value = CreateValueBox(slider.Value.ToString(CultureInfo.InvariantCulture), "%");
            value.Left = Math.Max(slider.Left + UIUtils.S(86), parent.Width - UIUtils.S(18) - value.Width);
            value.Top = UIUtils.S(y - 2);
            value.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            slider.Width = Math.Max(UIUtils.S(80), value.Left - slider.Left - UIUtils.S(14));
            slider.ValueChanged += (_, __) =>
            {
                if (IsUpdatingControls) return;
                double normalized = invert ? 1.0 - slider.Value / 100.0 : slider.Value / 100.0;
                Set(key, Math.Clamp(normalized, 0.0, 1.0));
                if (key == nameof(Settings.PanelBackgroundOpacity))
                    Set(nameof(Settings.Opacity), Math.Clamp(normalized, 0.0, 1.0));
                value.Text = slider.Value.ToString(CultureInfo.InvariantCulture) + " %";
                ReloadRuntimeDisplay();
            };
            RegisterRefresh(() =>
            {
                double current = Get(key, 1.0);
                int shown = invert ? (int)Math.Round((1.0 - current) * 100) : (int)Math.Round(current * 100);
                slider.Value = Math.Clamp(shown, 0, maxHiddenPercent);
                value.Text = slider.Value.ToString(CultureInfo.InvariantCulture) + " %";
            });
            RegisterSave(() =>
            {
                double normalized = invert ? 1.0 - slider.Value / 100.0 : slider.Value / 100.0;
                Set(key, Math.Clamp(normalized, 0.0, 1.0));
            });
            parent.Controls.Add(slider);
            parent.Controls.Add(value);
        }

        private void AddColorButtonRow(Control parent, string title, int x, int y, string key, string fallback, string emptyLabel, Action<string>? afterChanged)
        {
            parent.Controls.Add(CreateLabel(title, x, y + 2, 90, 24, UIFonts.Bold(9f), UIColors.TextMain));
            string InitialButtonText()
            {
                string current = Get(key, fallback);
                return string.IsNullOrWhiteSpace(current)
                    ? emptyLabel
                    : MainPanelSettingsPageModel.NormalizeColorHtml(current, fallback);
            }

            var button = new LiteButton(InitialButtonText())
            {
                Left = UIUtils.S(x + 110),
                Top = UIUtils.S(y - 4),
                Width = UIUtils.S(130),
                Height = UIUtils.S(30)
            };
            var clearButton = new LiteButton("清除", false)
            {
                Left = UIUtils.S(x + 248),
                Top = UIUtils.S(y - 4),
                Width = UIUtils.S(58),
                Height = UIUtils.S(30)
            };
            clearButton.Enabled = !string.IsNullOrWhiteSpace(Get(key, fallback));
            button.Click += (_, __) =>
            {
                string current = Get(key, fallback);
                using var dialog = new ColorDialog();
                if (!string.IsNullOrWhiteSpace(current))
                    dialog.Color = ParseColor(current, UIColors.CardBg);
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    string hex = MainPanelSettingsPageModel.FormatColorHtml(dialog.Color);
                    Set(key, hex);
                    button.Text = hex;
                    clearButton.Enabled = true;
                    afterChanged?.Invoke(hex);
                }
            };
            clearButton.Click += (_, __) =>
            {
                Set(key, string.Empty);
                button.Text = emptyLabel;
                clearButton.Enabled = false;
                afterChanged?.Invoke(string.Empty);
            };
            RegisterRefresh(() =>
            {
                button.Text = InitialButtonText();
                clearButton.Enabled = !string.IsNullOrWhiteSpace(Get(key, fallback));
            });
            RegisterSave(() =>
            {
                Set(key, string.Equals(button.Text, emptyLabel, StringComparison.Ordinal)
                    ? string.Empty
                    : MainPanelSettingsPageModel.NormalizeColorHtml(button.Text, fallback));
            });
            parent.Controls.Add(button);
            parent.Controls.Add(clearButton);
        }

        private void AddColorTile(Control parent, string title, string key, string fallback, int x, int y)
        {
            var tile = new RedesignCardPanel(UIColors.InputBg, radius: 6)
            {
                Left = UIUtils.S(x),
                Top = UIUtils.S(y),
                Width = UIUtils.S(160),
                Height = UIUtils.S(60),
                Padding = UIUtils.S(new Padding(12)),
                Cursor = Cursors.Hand
            };
            var titleLabel = CreateLabel(title, 12, 8, 100, 22, UIFonts.Bold(9f), UIColors.TextMain);
            string currentColor = MainPanelSettingsPageModel.NormalizeColorHtml(Get(key, fallback), fallback);
            var valueLabel = CreateLabel(currentColor, 12, 32, 100, 20, UIFonts.Bold(8.5f), UIColors.TextMain);
            var swatch = new Panel
            {
                Left = UIUtils.S(122),
                Top = UIUtils.S(14),
                Width = UIUtils.S(28),
                Height = UIUtils.S(28),
                BackColor = ParseColor(currentColor, Color.White)
            };
            tile.Controls.Add(titleLabel);
            tile.Controls.Add(valueLabel);
            tile.Controls.Add(swatch);
            tile.Click += (_, __) => PickColor();
            foreach (Control child in tile.Controls)
                child.Click += (_, __) => PickColor();

            void PickColor()
            {
                using var dialog = new ColorDialog { Color = swatch.BackColor };
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                    return;

                string hex = MainPanelSettingsPageModel.FormatColorHtml(dialog.Color);
                Set(key, hex);
                valueLabel.Text = hex;
                swatch.BackColor = dialog.Color;
                ReloadRuntimeDisplay();
            }

            RegisterRefresh(() =>
            {
                string hex = MainPanelSettingsPageModel.NormalizeColorHtml(Get(key, fallback), fallback);
                valueLabel.Text = hex;
                swatch.BackColor = ParseColor(hex, Color.White);
            });
            RegisterSave(() => Set(key, MainPanelSettingsPageModel.NormalizeColorHtml(valueLabel.Text, fallback)));
            parent.Controls.Add(tile);
        }

        private RedesignSwitch BindSwitch(string settingKey, bool fallback, Action<bool>? afterChanged)
        {
            return BindSwitch(() => Get(settingKey, fallback), value =>
            {
                Set(settingKey, value);
                afterChanged?.Invoke(value);
            });
        }

        private RedesignSwitch BindSwitch(Func<bool> get, Action<bool> set)
        {
            var toggle = new RedesignSwitch { Checked = get() };
            toggle.CheckedChanged += (_, __) =>
            {
                if (IsUpdatingControls) return;
                set(toggle.Checked);
            };
            RegisterRefresh(() => toggle.Checked = get());
            RegisterSave(() => set(toggle.Checked));
            return toggle;
        }

        private void BindSegment(RedesignSegmentedControl segment, Func<int> get, Action<int> set)
        {
            segment.SelectedIndex = get();
            segment.SelectedIndexChanged += (_, __) =>
            {
                if (IsUpdatingControls) return;
                set(segment.SelectedIndex);
            };
            RegisterRefresh(() => segment.SelectedIndex = get());
            RegisterSave(() => set(segment.SelectedIndex));
        }

        private Label CreateValueBox(string value, string unit)
        {
            return new Label
            {
                AutoSize = false,
                Width = UIUtils.S(84),
                Height = UIUtils.S(30),
                Text = value + (string.IsNullOrWhiteSpace(unit) ? "" : " " + unit),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = UIFonts.Bold(9f),
                ForeColor = UIColors.TextMain,
                BackColor = UIColors.InputBg
            };
        }

        private static Panel CreateDivider(int x, int y, int width)
        {
            return new Panel
            {
                Left = UIUtils.S(x),
                Top = UIUtils.S(y),
                Width = Math.Max(1, width),
                Height = 1,
                BackColor = UIColors.Border,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
        }

        private static Label CreateLabel(string text, int x, int y, int width, int height, Font font, Color color)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Left = UIUtils.S(x),
                Top = UIUtils.S(y),
                Width = UIUtils.S(width),
                Height = UIUtils.S(height),
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
        }

        private static void SelectCombo(LiteComboBox combo, string value)
        {
            if (combo.Items.Contains(value))
            {
                combo.SelectedItem = value;
                return;
            }

            combo.Items.Add(value);
            combo.SelectedItem = value;
        }

        private void ApplyTaskbarStylePreset(bool bold)
        {
            ApplyAssignments(MainPanelSettingsRules.BuildTaskbarStylePreset(bold));
            RefreshFromStore();
            ReloadRuntimeDisplay();
        }

        private void ApplyPreset(int preset)
        {
            ApplyAssignments(MainPanelSettingsRules.BuildTaskbarPreset(preset));
            _stylePresetSelection = preset;
            RefreshFromStore();
            ReloadRuntimeDisplay();
        }

        private int ResolveStylePresetSelection()
        {
            _stylePresetSelection = MainPanelSettingsPageModel.ResolveTaskbarPresetSelection(
                _stylePresetSelection,
                Get(nameof(Settings.TaskbarFontSize), Settings.DEFAULT_TB_SIZE_BOLD),
                Get(nameof(Settings.TaskbarFontBold), true),
                Get(nameof(Settings.TaskbarItemSpacing), Settings.DEFAULT_TB_GAP),
                Get(nameof(Settings.TaskbarInnerSpacing), Settings.DEFAULT_TB_INNER_BOLD),
                Get(nameof(Settings.TaskbarVerticalPadding), Settings.DEFAULT_TB_VOFF),
                Get(nameof(Settings.TaskbarSingleLine), false),
                Get(nameof(Settings.TaskbarCustomStyle), true),
                Get(nameof(Settings.TaskbarColorBg), "#001E3D"),
                Get(nameof(Settings.TaskbarColorLabel), "#FFFFFF"),
                Get(nameof(Settings.TaskbarColorSafe), "#00CC66"),
                Get(nameof(Settings.TaskbarColorWarn), "#FFFF00"),
                Get(nameof(Settings.TaskbarColorCrit), "#FF4444"));
            return _stylePresetSelection;
        }

        private MainPanelSafeVisibilityResult EnsureSafeVisibility()
        {
            MainPanelSafeVisibilityResult visibility = MainPanelSettingsRules.ResolveSafeVisibility(
                Get(nameof(Settings.HideMainForm), false),
                Get(nameof(Settings.HideTrayIcon), false),
                Get(nameof(Settings.ShowTaskbar), true),
                Get(nameof(Settings.ClickThrough), false),
                taskbarClickThrough: false);
            if (visibility.RequiresCorrection)
            {
                Set(nameof(Settings.HideMainForm), visibility.HideMainForm);
                Set(nameof(Settings.ShowTaskbar), visibility.ShowTaskbar);
                RefreshFromStore();
            }

            return visibility;
        }

        private void ApplySafeVisibilityToRuntime(MainPanelSafeVisibilityResult visibility)
        {
            if (visibility.HideMainForm)
                MainForm?.HideMainWindow();
            else
                MainForm?.ShowMainWindow();

            MainForm?.ToggleTaskbar(visibility.ShowTaskbar);
        }

        private void ApplyAssignments(IEnumerable<MainPanelSettingAssignment> assignments)
        {
            foreach (MainPanelSettingAssignment assignment in assignments)
                Set(assignment.Key, assignment.Value);
        }

        private void ReloadRuntimeDisplay()
        {
            if (Config is not null)
                TaskbarRenderer.ReloadStyle(Config);

            UI?.RebuildLayout();
            MainForm?.RequestLayeredRender();
        }

        private static Color ParseColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return fallback;

            try
            {
                return ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
