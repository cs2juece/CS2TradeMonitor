using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.SystemServices;
using CS2TradeMonitor.src.UI.Controls;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class AuthorNoteDialog : Form
    {
        private static readonly string[] SectionOrder =
        {
            "关于作者",
            "为什么做这个工具",
            "关于开发",
            "关于自动化",
            "关于免费和开源",
            "最后"
        };

        private static readonly string[] StrongParagraphs =
        {
            "一开始只是为了方便自己交易使用，后来慢慢增加了一些自己觉得有价值的功能。",
            "如果它能够帮助更多交易玩家减少重复操作，把更多时间放在真正重要的事情上，那就是它存在的意义。",
            "工具只能提高效率。",
            "最终决定结果的，永远是交易者自己的判断和执行。",
            "CS2TradeMonitor 是一个免费开源项目。",
            "这个软件最开始只是我自己交易过程中使用的小工具。",
            "如果它能在某个时候，帮你省下一点时间，少错过一次报价，少做一次重复操作，那就是它最大的价值。"
        };

        private static readonly string[] InlineEmphasis =
        {
            "CS2 饰品交易",
            "真正适合自己的工具",
            "AI 辅助下一步一步完成",
            "真实交易过程中的需求",
            "官方渠道"
        };

        private readonly string _bodyText;
        private readonly IReadOnlyList<StorySection> _sections;
        private readonly Panel _scrollHost;
        private readonly FlowLayoutPanel _storyFlow;
        private int _emphasisCount;

        public AuthorNoteDialog()
        {
            _bodyText = LoadContent();
            _sections = ParseSections(_bodyText);

            Text = "CS2交易监控";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = UIUtils.S(new Size(860, 720));
            MinimumSize = UIUtils.S(new Size(680, 520));
            BackColor = UIColors.MainBg;
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = UIColors.MainBg,
                Padding = UIUtils.S(new Padding(24, 20, 24, 14))
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, UIUtils.S(52)));
            Controls.Add(root);

            _scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = UIColors.MainBg,
                Padding = new Padding(0, 0, UIUtils.S(8), 0)
            };
            root.Controls.Add(_scrollHost, 0, 0);

            _storyFlow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = UIColors.MainBg,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };
            _scrollHost.Controls.Add(_storyFlow);
            BuildStory();

            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UIColors.MainBg,
                Padding = new Padding(0, UIUtils.S(12), 0, 0)
            };
            var closeButton = new LiteButton("我知道了", true)
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Size = UIUtils.S(new Size(104, 34))
            };
            closeButton.Click += (_, __) => Close();
            footer.Controls.Add(closeButton);
            footer.Layout += (_, __) => closeButton.Location = new Point(
                Math.Max(0, footer.ClientSize.Width - closeButton.Width),
                footer.Padding.Top);
            root.Controls.Add(footer, 0, 1);

            _scrollHost.Layout += (_, __) => LayoutStory();
            UIColors.ApplyNativeThemeRecursively(this);
            ApplyStoryColors();
            Shown += (_, __) =>
            {
                LayoutStory();
                ActiveControl = closeButton;
                _scrollHost.AutoScrollPosition = Point.Empty;
                BeginInvoke(new Action(() =>
                {
                    _scrollHost.AutoScrollPosition = Point.Empty;
                    _scrollHost.VerticalScroll.Value = 0;
                }));
            };
        }

        private void BuildStory()
        {
            for (int sectionIndex = 0; sectionIndex < _sections.Count; sectionIndex++)
            {
                StorySection section = _sections[sectionIndex];
                if (sectionIndex > 0)
                    _storyFlow.Controls.Add(CreateDivider());

                _storyFlow.Controls.Add(new Label
                {
                    Text = section.Title,
                    AutoSize = false,
                    Height = UIUtils.S(28),
                    Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold),
                    ForeColor = UIColors.TextMain,
                    BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.TopLeft,
                    Margin = new Padding(0, 0, 0, UIUtils.S(5))
                });

                foreach (string paragraph in section.Paragraphs)
                {
                    _storyFlow.Controls.Add(CreateParagraph(paragraph, section.Title == "最后" && paragraph.StartsWith("如果项目中存在", StringComparison.Ordinal)));
                }
            }
        }

        private Control CreateParagraph(string text, bool legalNote)
        {
            bool strong = StrongParagraphs.Contains(text, StringComparer.Ordinal);
            float fontSize = legalNote ? 8.5F : strong ? 9.5F : 9F;
            FontStyle style = strong ? FontStyle.Bold : FontStyle.Regular;
            var paragraph = new RichTextBox
            {
                Text = text,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                DetectUrls = false,
                ScrollBars = RichTextBoxScrollBars.None,
                WordWrap = true,
                TabStop = false,
                BackColor = UIColors.MainBg,
                ForeColor = legalNote ? UIColors.TextSub : UIColors.TextMain,
                Font = new Font("Microsoft YaHei UI", fontSize, style),
                Margin = new Padding(0, 0, 0, UIUtils.S(strong ? 9 : 6)),
                Tag = new ParagraphStyle(strong, legalNote)
            };

            paragraph.SelectAll();
            paragraph.SelectionFont = paragraph.Font;
            paragraph.SelectionColor = paragraph.ForeColor;
            if (strong)
                _emphasisCount++;

            foreach (string phrase in InlineEmphasis)
            {
                int index = text.IndexOf(phrase, StringComparison.Ordinal);
                if (index < 0)
                    continue;

                paragraph.Select(index, phrase.Length);
                paragraph.SelectionFont = new Font(paragraph.Font, FontStyle.Bold);
                _emphasisCount++;
            }

            paragraph.Select(0, 0);
            return paragraph;
        }

        private static Control CreateDivider()
        {
            return new Panel
            {
                Height = 1,
                BackColor = UIColors.Border,
                Margin = new Padding(0, UIUtils.S(8), 0, UIUtils.S(14))
            };
        }

        private void LayoutStory()
        {
            int availableWidth = Math.Max(UIUtils.S(420), _scrollHost.ClientSize.Width - _scrollHost.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth);
            int storyWidth = Math.Min(UIUtils.S(780), availableWidth);
            _storyFlow.SuspendLayout();
            _storyFlow.Width = storyWidth;
            _storyFlow.Location = new Point(Math.Max(0, (availableWidth - storyWidth) / 2), 0);

            foreach (Control control in _storyFlow.Controls)
            {
                control.Width = storyWidth;
                if (control is RichTextBox paragraph)
                {
                    paragraph.Height = MeasureRichTextHeight(paragraph, storyWidth);
                }
            }

            _storyFlow.ResumeLayout(true);
            _scrollHost.AutoScrollMinSize = new Size(0, _storyFlow.PreferredSize.Height);
        }

        private void ApplyStoryColors()
        {
            _scrollHost.BackColor = UIColors.MainBg;
            _storyFlow.BackColor = UIColors.MainBg;
            foreach (RichTextBox paragraph in _storyFlow.Controls.OfType<RichTextBox>())
            {
                paragraph.BackColor = UIColors.MainBg;
                paragraph.ForeColor = paragraph.Tag is ParagraphStyle { LegalNote: true }
                    ? UIColors.TextSub
                    : UIColors.TextMain;
            }
        }

        private static int MeasureRichTextHeight(RichTextBox paragraph, int width)
        {
            paragraph.Width = width;
            paragraph.RightMargin = Math.Max(1, width - UIUtils.S(8));
            int measuredHeight = MeasureTextHeight(paragraph.Text, paragraph.Font, width) + UIUtils.S(10);
            if (paragraph.TextLength == 0)
                return measuredHeight;

            Point lastCharacter = paragraph.GetPositionFromCharIndex(paragraph.TextLength - 1);
            int renderedHeight = lastCharacter.Y + paragraph.Font.Height + UIUtils.S(8);
            return Math.Max(measuredHeight, renderedHeight);
        }

        private static int MeasureTextHeight(string text, Font font, int width)
        {
            Size measured = TextRenderer.MeasureText(
                text,
                font,
                new Size(Math.Max(1, width - UIUtils.S(2)), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
            return Math.Max(UIUtils.S(24), measured.Height);
        }

        private static string LoadContent()
        {
            string path = Path.Combine(InstallationPaths.ResourcesDirectory, "author-note.txt");
            try
            {
                return File.ReadAllText(path, new UTF8Encoding(false));
            }
            catch
            {
                return "作者自白内容暂时无法读取。";
            }
        }

        private static IReadOnlyList<StorySection> ParseSections(string content)
        {
            string normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
            var sections = new List<StorySection>();
            string currentTitle = string.Empty;
            var paragraphLines = new List<string>();
            var paragraphs = new List<string>();

            void FlushParagraph()
            {
                if (paragraphLines.Count == 0)
                    return;

                paragraphs.Add(string.Join(Environment.NewLine, paragraphLines));
                paragraphLines.Clear();
            }

            void FlushSection()
            {
                FlushParagraph();
                if (currentTitle.Length > 0)
                    sections.Add(new StorySection(currentTitle, paragraphs.ToArray()));
                paragraphs.Clear();
            }

            foreach (string rawLine in normalized.Split('\n'))
            {
                string line = rawLine.TrimEnd();
                if (line == "作者自白" && currentTitle.Length == 0)
                    continue;

                if (SectionOrder.Contains(line, StringComparer.Ordinal))
                {
                    FlushSection();
                    currentTitle = line;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph();
                    continue;
                }

                paragraphLines.Add(line);
            }

            FlushSection();
            return sections;
        }

        internal string GetBodyTextForTesting() => _bodyText;

        internal string[] GetSectionTitlesForTesting() => _sections.Select(section => section.Title).ToArray();

        internal int GetEmphasisCountForTesting() => _emphasisCount;

        internal string[] GetButtonTextsForTesting()
        {
            return Controls
                .Cast<Control>()
                .SelectMany(GetDescendants)
                .OfType<LiteButton>()
                .Select(button => button.Text)
                .ToArray();
        }

        private static IEnumerable<Control> GetDescendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in GetDescendants(child))
                    yield return descendant;
            }
        }

        private sealed record StorySection(string Title, IReadOnlyList<string> Paragraphs);

        private sealed record ParagraphStyle(bool Strong, bool LegalNote);
    }
}
