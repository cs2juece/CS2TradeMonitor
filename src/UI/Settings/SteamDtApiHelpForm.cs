using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    internal sealed class SteamDtApiHelpForm : Form
    {
        private const string SteamDtSettingsUrl = SteamDtUrls.SettingsPage;

        public SteamDtApiHelpForm()
        {
            Text = "SteamDT API Key 填写说明";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = UIUtils.S(new Size(1000, 780));
            MinimumSize = UIUtils.S(new Size(820, 620));
            BackColor = UIColors.MainBg;
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = UIColors.MainBg,
                Padding = UIUtils.S(new Padding(20, 18, 20, 16))
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            root.Controls.Add(CreateHeader(), 0, 0);
            root.Controls.Add(CreateContent(), 0, 1);
            root.Controls.Add(CreateFooter(), 0, 2);
            UIColors.ApplyNativeThemeRecursively(this);

            Shown += (_, __) =>
            {
                BringToFront();
                Activate();
            };
        }

        private Control CreateHeader()
        {
            var header = new Panel
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                BackColor = Color.Transparent,
                Height = UIUtils.S(132),
                Padding = new Padding(0, 0, 0, UIUtils.S(12))
            };

            var title = new Label
            {
                Text = "SteamDT API Key 图文指引",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                Location = new Point(0, 0)
            };
            header.Controls.Add(title);

            var summary = new Label
            {
                Text = "登录 SteamDT 后进入 API 管理，新用户先开通 API，再复制右侧 API_KEY 粘贴到本页输入框。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                Location = new Point(0, UIUtils.S(34)),
                Height = UIUtils.S(24)
            };
            header.Controls.Add(summary);

            var openButton = new LiteButton("打开 SteamDT 设置页", true)
            {
                Width = UIUtils.S(150),
                Height = UIUtils.S(32)
            };
            openButton.Click += (_, __) => OpenUrl(SteamDtSettingsUrl);
            header.Controls.Add(openButton);

            var noteBox = new Panel
            {
                Location = new Point(0, UIUtils.S(68)),
                Height = UIUtils.S(48),
                Padding = UIUtils.S(new Padding(12, 6, 12, 6)),
                BackColor = Color.Transparent
            };
            noteBox.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = UIUtils.RoundRect(new Rectangle(0, 0, noteBox.Width - 1, noteBox.Height - 1), UIUtils.S(6));
                using var fill = new SolidBrush(UIColors.IsDark ? Color.FromArgb(45, 28, 32) : Color.FromArgb(255, 246, 246));
                using var border = new Pen(UIColors.IsDark ? Color.FromArgb(86, 44, 50) : Color.FromArgb(255, 210, 210));
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            };
            var note = new Label
            {
                Text = "安全说明：API Key 不是 Steam 密码。CS2交易监控只用它读取 SteamDT 大盘指数，不会读取你的 Steam 账号密码，也不会操作库存、交易或登录状态。泄露后可在 SteamDT 后台重新生成。",
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
                ForeColor = UIColors.TextCrit,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            noteBox.Controls.Add(note);
            header.Controls.Add(noteBox);
            header.Layout += (_, __) =>
            {
                summary.Width = Math.Max(1, header.ClientSize.Width - openButton.Width - UIUtils.S(16));
                noteBox.Width = Math.Max(1, header.ClientSize.Width);
                openButton.Location = new Point(header.ClientSize.Width - openButton.Width, UIUtils.S(30));
            };

            return header;
        }

        private Control CreateContent()
        {
            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UIColors.MainBg,
                Padding = UIUtils.S(new Padding(2, 0, 8, 0))
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = UIColors.MainBg,
                Padding = new Padding(0)
            };
            scroll.Controls.Add(flow);

            flow.Controls.Add(CreateStep(
                "1",
                "左侧菜单选择 API管理",
                "进入 SteamDT 账号设置页面后，在左侧菜单点击“API管理”。",
                "steamdt-api-menu.png",
                new Size(512, 320),
                UIUtils.S(245)));

            flow.Controls.Add(CreateStep(
                "2",
                "新用户点击开通 API 功能",
                "如果右侧还没有 API_KEY，先点击蓝色“开通API功能”。这是 SteamDT 后台开通接口能力，不需要填写 Steam 密码。",
                "steamdt-api-open.png",
                new Size(1100, 620),
                UIUtils.S(300)));

            flow.Controls.Add(CreateStep(
                "3",
                "右侧复制 API_KEY",
                "在右侧找到“已开通API功能”下方的 API_KEY，点击“复制”，回到设置页粘贴到 SteamDT API Key 输入框。",
                "steamdt-api-key.png",
                new Size(900, 420),
                UIUtils.S(285)));

            scroll.Layout += (_, __) =>
            {
                int width = Math.Max(1, scroll.ClientSize.Width - scroll.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                flow.Width = width;
                foreach (Control child in flow.Controls)
                {
                    child.Width = width;
                    child.PerformLayout();
                }
            };

            return scroll;
        }

        private Control CreateStep(string stepNo, string title, string description, string imageName, Size originalImageSize, int maxImageHeight)
        {
            var step = new Panel
            {
                AutoSize = false,
                BackColor = UIColors.CardBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(14)),
                Padding = UIUtils.S(new Padding(14))
            };
            step.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = UIUtils.RoundRect(new Rectangle(0, 0, step.Width - 1, step.Height - 1), UIUtils.S(8));
                using var fill = new SolidBrush(UIColors.CardBg);
                using var border = new Pen(UIColors.Border);
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            };

            var badge = new Label
            {
                Text = "",
                Tag = stepNo,
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            badge.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = UIUtils.RoundRect(new Rectangle(0, 0, badge.Width - 1, badge.Height - 1), UIUtils.S(14));
                using var fill = new SolidBrush(UIColors.Primary);
                e.Graphics.FillPath(fill, path);
                TextRenderer.DrawText(e.Graphics, badge.Tag?.ToString() ?? "", badge.Font, badge.ClientRectangle, badge.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            step.Controls.Add(badge);

            var titleLabel = new Label
            {
                Text = title,
                AutoSize = false,
                Height = UIUtils.S(28),
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                TextAlign = ContentAlignment.MiddleLeft
            };
            step.Controls.Add(titleLabel);

            var descLabel = new Label
            {
                Text = description,
                AutoSize = false,
                Height = UIUtils.S(40),
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true
            };
            step.Controls.Add(descLabel);

            var imagePanel = new Panel
            {
                BackColor = UIColors.IsDark ? UIColors.MainBg : Color.FromArgb(248, 250, 253),
                Padding = UIUtils.S(new Padding(10))
            };
            imagePanel.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = UIUtils.RoundRect(new Rectangle(0, 0, imagePanel.Width - 1, imagePanel.Height - 1), UIUtils.S(6));
                using var fill = new SolidBrush(UIColors.IsDark ? UIColors.MainBg : Color.FromArgb(248, 250, 253));
                using var border = new Pen(UIColors.IsDark ? UIColors.Border : Color.FromArgb(230, 235, 245));
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            };
            step.Controls.Add(imagePanel);

            var imageBox = new PictureBox
            {
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                SizeMode = PictureBoxSizeMode.Zoom
            };
            imageBox.Image = LoadHelpImage(imageName);
            imagePanel.Controls.Add(imageBox);

            var fallback = new Label
            {
                Text = "指引图片缺失，请查看发布包 resources/api-help 目录。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextWarn,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = imageBox.Image == null
            };
            imagePanel.Controls.Add(fallback);

            step.Layout += (_, __) =>
            {
                int padding = step.Padding.Left;
                int width = Math.Max(1, step.ClientSize.Width - step.Padding.Horizontal);
                int badgeSize = UIUtils.S(28);
                int textX = padding + badgeSize + UIUtils.S(10);
                int textWidth = Math.Max(1, width - badgeSize - UIUtils.S(10));
                int y = step.Padding.Top;

                badge.SetBounds(padding, y + UIUtils.S(2), badgeSize, badgeSize);
                titleLabel.SetBounds(textX, y, textWidth, titleLabel.Height);
                descLabel.SetBounds(textX, titleLabel.Bottom, textWidth, descLabel.Height);

                int imageTop = descLabel.Bottom + UIUtils.S(8);
                int imageWidth = Math.Max(1, width);
                double ratio = originalImageSize.Height / Math.Max(1.0, originalImageSize.Width);
                int imageHeight = Math.Min(maxImageHeight, Math.Max(UIUtils.S(180), (int)Math.Round(imageWidth * ratio)));
                imagePanel.SetBounds(padding, imageTop, imageWidth, imageHeight);
                imageBox.SetBounds(imagePanel.Padding.Left, imagePanel.Padding.Top,
                    Math.Max(1, imagePanel.ClientSize.Width - imagePanel.Padding.Horizontal),
                    Math.Max(1, imagePanel.ClientSize.Height - imagePanel.Padding.Vertical));
                fallback.SetBounds(imageBox.Bounds.X, imageBox.Bounds.Y, imageBox.Width, imageBox.Height);
                step.Height = imagePanel.Bottom + step.Padding.Bottom;
            };

            return step;
        }

        private Control CreateFooter()
        {
            var footer = new Panel
            {
                Dock = DockStyle.Top,
                Height = UIUtils.S(46),
                BackColor = Color.Transparent,
                Padding = new Padding(0, UIUtils.S(14), 0, 0)
            };

            var closeButton = new LiteButton("关闭", true)
            {
                Width = UIUtils.S(92),
                Height = UIUtils.S(32)
            };
            closeButton.Click += (_, __) => Close();
            footer.Controls.Add(closeButton);
            footer.Layout += (_, __) =>
            {
                closeButton.Location = new Point(footer.ClientSize.Width - closeButton.Width, UIUtils.S(10));
            };

            return footer;
        }

        private static Image? LoadHelpImage(string imageName)
        {
            string path = Path.Combine(InstallationPaths.ResourcesDirectory, "api-help", imageName);
            if (!File.Exists(path)) return null;

            using var source = Image.FromFile(path);
            return new Bitmap(source);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                GlobalPromptService.Show("打开 SteamDT 设置页失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
