using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using CS2TradeMonitor.Application.Market;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.SettingsPage
{
    internal sealed class CsqaqApiHelpForm : Form
    {
        private const string CsqaqHomeUrl = CsqaqUrls.WebBase;
        private const string CsqaqDocsUrl = CsqaqUrls.DocsApiToken;

        public CsqaqApiHelpForm()
        {
            Text = "QAQ API Token 填写说明";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = UIUtils.S(new Size(860, 560));
            MinimumSize = UIUtils.S(new Size(720, 500));
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
                Height = UIUtils.S(118),
                Padding = new Padding(0, 0, 0, UIUtils.S(12))
            };

            var title = new Label
            {
                Text = "QAQ API Token 接入说明",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                ForeColor = UIColors.TextMain,
                Location = new Point(0, 0)
            };
            header.Controls.Add(title);

            var summary = new Label
            {
                Text = "登录 CSQAQ 后，从右上角头像进入“查看API令牌”，复制 Token 到本页输入框；如需要白名单 IP，可在弹窗里手动绑定或自动获取。",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                Location = new Point(0, UIUtils.S(34)),
                Height = UIUtils.S(36),
                TextAlign = ContentAlignment.MiddleLeft
            };
            header.Controls.Add(summary);

            var openHomeButton = new LiteButton("打开 CSQAQ", false)
            {
                Width = UIUtils.S(120),
                Height = UIUtils.S(32)
            };
            openHomeButton.Click += (_, __) => OpenUrl(CsqaqHomeUrl);
            header.Controls.Add(openHomeButton);

            var openDocsButton = new LiteButton("打开接入文档", true)
            {
                Width = UIUtils.S(128),
                Height = UIUtils.S(32)
            };
            openDocsButton.Click += (_, __) => OpenUrl(CsqaqDocsUrl);
            header.Controls.Add(openDocsButton);

            header.Layout += (_, __) =>
            {
                int gap = UIUtils.S(8);
                int y = UIUtils.S(76);
                openDocsButton.Location = new Point(header.ClientSize.Width - openDocsButton.Width, y);
                openHomeButton.Location = new Point(openDocsButton.Left - gap - openHomeButton.Width, y);
                summary.Width = Math.Max(1, header.ClientSize.Width);
            };

            return header;
        }

        private Control CreateContent()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UIColors.MainBg,
                AutoScroll = true,
                Padding = UIUtils.S(new Padding(2, 0, 8, 0))
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = UIColors.MainBg
            };
            panel.Controls.Add(flow);

            flow.Controls.Add(CreateStep(
                "1",
                "右上角头像 -> 查看API令牌",
                "打开 CSQAQ.COM 并登录账号，点击右上角头像，在弹出的账号面板里点“查看API令牌”。旁边的“API文档”也可以打开官方接入说明。"));

            flow.Controls.Add(CreateStep(
                "2",
                "复制 API Token",
                "弹窗顶部会显示“您的API TOKEN接入令牌”，点击右侧复制按钮，把 Token 粘贴到本页的“QAQ API Token（选填）”输入框。"));

            flow.Controls.Add(CreateStep(
                "3",
                "按需绑定白名单 IP",
                "如果 CSQAQ 要求白名单 IP，在弹窗中选择“自动获取”或手动输入当前公网 IP 后点击“手动绑定”。没有 Token 时软件仍会走公开接口。"));

            flow.Controls.Add(CreateDocCard());

            panel.Layout += (_, __) =>
            {
                int width = Math.Max(1, panel.ClientSize.Width - panel.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
                flow.Width = width;
                foreach (Control child in flow.Controls)
                {
                    child.Width = width;
                    child.PerformLayout();
                }
            };

            return panel;
        }

        private Control CreateStep(string stepNo, string title, string description)
        {
            var step = new Panel
            {
                AutoSize = false,
                BackColor = UIColors.CardBg,
                Margin = new Padding(0, 0, 0, UIUtils.S(12)),
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
                Font = new Font("Microsoft YaHei UI", 9F),
                ForeColor = UIColors.TextSub,
                TextAlign = ContentAlignment.TopLeft
            };
            step.Controls.Add(descLabel);

            step.Layout += (_, __) =>
            {
                int padding = step.Padding.Left;
                int badgeSize = UIUtils.S(28);
                int textX = padding + badgeSize + UIUtils.S(10);
                int textWidth = Math.Max(1, step.ClientSize.Width - step.Padding.Horizontal - badgeSize - UIUtils.S(10));
                int y = step.Padding.Top;

                badge.SetBounds(padding, y + UIUtils.S(2), badgeSize, badgeSize);
                titleLabel.SetBounds(textX, y, textWidth, titleLabel.Height);
                descLabel.SetBounds(textX, titleLabel.Bottom + UIUtils.S(2), textWidth, UIUtils.S(54));
                step.Height = descLabel.Bottom + step.Padding.Bottom;
            };

            return step;
        }

        private Control CreateDocCard()
        {
            var card = new Panel
            {
                AutoSize = false,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, UIUtils.S(8)),
                Padding = UIUtils.S(new Padding(14))
            };
            card.Paint += (_, e) =>
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var path = UIUtils.RoundRect(new Rectangle(0, 0, card.Width - 1, card.Height - 1), UIUtils.S(8));
                using var fill = new SolidBrush(UIColors.IsDark ? Color.FromArgb(22, 41, 58) : Color.FromArgb(232, 244, 255));
                using var border = new Pen(UIColors.IsDark ? Color.FromArgb(42, 76, 104) : Color.FromArgb(188, 220, 255));
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            };

            var label = new Label
            {
                Text = "官方接入文档：docs.csqaq.com/doc-4588854",
                AutoSize = false,
                Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
                ForeColor = UIColors.IsDark ? Color.FromArgb(142, 196, 255) : UIColors.Primary,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            card.Controls.Add(label);

            var openButton = new LiteButton("打开文档", true)
            {
                Width = UIUtils.S(104),
                Height = UIUtils.S(32)
            };
            openButton.Click += (_, __) => OpenUrl(CsqaqDocsUrl);
            card.Controls.Add(openButton);

            card.Layout += (_, __) =>
            {
                int mid = card.ClientSize.Height / 2;
                openButton.Location = new Point(card.ClientSize.Width - card.Padding.Right - openButton.Width, mid - openButton.Height / 2);
                label.SetBounds(card.Padding.Left, card.Padding.Top,
                    Math.Max(1, openButton.Left - card.Padding.Left - UIUtils.S(12)),
                    Math.Max(UIUtils.S(24), card.ClientSize.Height - card.Padding.Vertical));
                card.Height = UIUtils.S(64);
            };

            return card;
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

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                GlobalPromptService.Show("打开链接失败：" + ex.Message, "CS2交易监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
