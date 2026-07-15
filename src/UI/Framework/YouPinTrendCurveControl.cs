using CS2TradeMonitor.Domain.YouPin;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CS2TradeMonitor.src.UI.Framework
{
    internal sealed class YouPinTrendCurveControl : Panel
    {
        private List<YouPinDailyPnl> _points;
        private Color _curveColor;
        private Color _riseColor;
        private Color _fallColor;
        private Color _textColor;
        private Color _subTextColor;
        private float _fontSize;
        private readonly Label _titleLabel;
        private readonly List<(YouPinDailyPnl Point, PointF Location)> _hitPoints = new();
        private RectangleF _plotBounds = RectangleF.Empty;
        private int _hoverIndex = -1;

        public YouPinTrendCurveControl(List<YouPinDailyPnl> points, Color curveColor, Color riseColor, Color fallColor, Color textColor, Color subTextColor, float fontSize)
        {
            _points = points ?? new List<YouPinDailyPnl>();
            _curveColor = curveColor;
            _riseColor = riseColor;
            _fallColor = fallColor;
            _textColor = textColor;
            _subTextColor = subTextColor;
            _fontSize = Math.Clamp(fontSize, 7f, 16f);

            BackColor = Color.Transparent;
            DoubleBuffered = true;
            ResizeRedraw = true;
            Cursor = Cursors.Cross;

            _titleLabel = new Label
            {
                Text = "每日收益",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", Math.Max(8f, _fontSize + 0.5f), FontStyle.Bold),
                ForeColor = _subTextColor,
                BackColor = Color.Transparent
            };
            Controls.Add(_titleLabel);

            Layout += (_, __) =>
            {
                _titleLabel.Location = new Point(UIUtils.S(14), UIUtils.S(10));
            };
        }

        public void UpdateData(List<YouPinDailyPnl> points, Color curveColor, Color riseColor, Color fallColor, Color textColor, Color subTextColor, float fontSize)
        {
            _points = points ?? new List<YouPinDailyPnl>();
            _curveColor = curveColor;
            _riseColor = riseColor;
            _fallColor = fallColor;
            _textColor = textColor;
            _subTextColor = subTextColor;
            _fontSize = Math.Clamp(fontSize, 7f, 16f);
            BackColor = Color.Transparent;
            _titleLabel.ForeColor = _subTextColor;
            _hoverIndex = -1;
            _hitPoints.Clear();
            float titleSize = Math.Max(8f, _fontSize + 0.5f);
            if (Math.Abs(_titleLabel.Font.Size - titleSize) > 0.01f)
            {
                var oldFont = _titleLabel.Font;
                _titleLabel.Font = new Font("Microsoft YaHei UI", titleSize, FontStyle.Bold);
                oldFont.Dispose();
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int marginLeft = UIUtils.S(52);
            int marginRight = UIUtils.S(18);
            int marginTop = UIUtils.S(42);
            int marginBottom = UIUtils.S(30);
            int usableW = Width - marginLeft - marginRight;
            int usableH = Height - marginTop - marginBottom;
            if (usableW <= UIUtils.S(50) || usableH <= UIUtils.S(35))
                return;

            var sortedPoints = _points
                .Where(x => x.HasProfitAndLoss)
                .OrderBy(x => x.Date)
                .ToList();

            if (sortedPoints.Count < 2)
            {
                DrawEmptyState(e.Graphics, sortedPoints, marginLeft, marginRight, marginTop, marginBottom, usableW, usableH);
                return;
            }

            double minVal = sortedPoints.Min(GetCurveValue);
            double maxVal = sortedPoints.Max(GetCurveValue);
            if (minVal > 0) minVal = 0;
            if (maxVal < 0) maxVal = 0;
            if (Math.Abs(maxVal - minVal) < 0.01)
            {
                minVal -= 10;
                maxVal += 10;
            }
            else
            {
                double diff = maxVal - minVal;
                minVal -= diff * 0.15;
                maxVal += diff * 0.15;
            }

            PointF MapPoint(int index, double value)
            {
                float x = marginLeft + (float)index / (sortedPoints.Count - 1) * usableW;
                float y = MapY(value, minVal, maxVal, marginTop, usableH);
                return new PointF(x, y);
            }

            var mapped = sortedPoints.Select((p, i) => MapPoint(i, GetCurveValue(p))).ToArray();
            DrawGrid(e.Graphics, marginLeft, marginRight, marginTop, usableH, minVal, maxVal);
            DrawCurveFill(e.Graphics, mapped, marginTop, usableH);

            using (var linePen = new Pen(_curveColor, 2F))
            {
                e.Graphics.DrawLines(linePen, mapped);
            }

            UpdateHitPoints(sortedPoints, mapped, marginLeft, marginTop, usableW, usableH);
            DrawEndPoint(e.Graphics, mapped[^1]);
            DrawLabels(e.Graphics, sortedPoints, minVal, maxVal, marginLeft, marginRight, marginTop, marginBottom, usableW, usableH);
            DrawHoverIndicator(e.Graphics, marginTop, usableH);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            int nextHover = FindNearestPointIndex(e.Location);
            if (nextHover == _hoverIndex)
                return;

            _hoverIndex = nextHover;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex < 0)
                return;

            _hoverIndex = -1;
            Invalidate();
        }

        private void UpdateHitPoints(List<YouPinDailyPnl> sortedPoints, PointF[] mapped, int marginLeft, int marginTop, int usableW, int usableH)
        {
            _hitPoints.Clear();
            for (int i = 0; i < sortedPoints.Count && i < mapped.Length; i++)
                _hitPoints.Add((sortedPoints[i], mapped[i]));

            _plotBounds = new RectangleF(marginLeft, marginTop, usableW, usableH);
            if (_hoverIndex >= _hitPoints.Count)
                _hoverIndex = -1;
        }

        private int FindNearestPointIndex(Point location)
        {
            if (_hitPoints.Count == 0 || _plotBounds == RectangleF.Empty)
                return -1;

            var hoverBounds = RectangleF.Inflate(_plotBounds, UIUtils.S(10), UIUtils.S(16));
            if (!hoverBounds.Contains(location))
                return -1;

            int nearest = -1;
            float nearestDistance = float.MaxValue;
            for (int i = 0; i < _hitPoints.Count; i++)
            {
                float distance = Math.Abs(location.X - _hitPoints[i].Location.X);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = i;
                }
            }

            return nearest;
        }

        private void DrawHoverIndicator(Graphics graphics, int marginTop, int usableH)
        {
            if (_hoverIndex < 0 || _hoverIndex >= _hitPoints.Count)
                return;

            var (point, location) = _hitPoints[_hoverIndex];
            double value = GetCurveValue(point);
            using (var guidePen = new Pen(Color.FromArgb(110, _subTextColor)))
            {
                guidePen.DashPattern = new float[] { 3, 3 };
                graphics.DrawLine(guidePen, location.X, marginTop, location.X, marginTop + usableH);
            }

            using (var markerBrush = new SolidBrush(UIColors.CardBg))
                graphics.FillEllipse(markerBrush, location.X - 5f, location.Y - 5f, 10f, 10f);
            using (var markerPen = new Pen(_curveColor, 2f))
                graphics.DrawEllipse(markerPen, location.X - 5f, location.Y - 5f, 10f, 10f);

            string dateText = $"日期：{FormatCurveFullDate(point.Date)}";
            string valueText = $"当日收益/亏损：{FormatCurveMoney(value)}";
            string percentText = $"涨跌百分比：{FormatCurvePercent(GetCurvePercent(point))}";
            string statusText = $"状态：{FormatPnlStatus(value)}";
            using var titleFont = new Font("Microsoft YaHei UI", Math.Max(8f, _fontSize), FontStyle.Bold);
            using var valueFont = new Font("Microsoft YaHei UI", Math.Max(7f, _fontSize - 0.5f));
            var dateSize = graphics.MeasureString(dateText, titleFont);
            var valueSize = graphics.MeasureString(valueText, valueFont);
            var percentSize = graphics.MeasureString(percentText, valueFont);
            var statusSize = graphics.MeasureString(statusText, valueFont);

            int paddingX = UIUtils.S(10);
            int paddingY = UIUtils.S(7);
            float tooltipW = Math.Max(Math.Max(dateSize.Width, valueSize.Width), Math.Max(percentSize.Width, statusSize.Width)) + paddingX * 2;
            float tooltipH = dateSize.Height + valueSize.Height + percentSize.Height + statusSize.Height + paddingY * 2 + UIUtils.S(6);
            float tooltipX = location.X + UIUtils.S(12);
            if (tooltipX + tooltipW > Width - UIUtils.S(8))
                tooltipX = location.X - tooltipW - UIUtils.S(12);
            tooltipX = Math.Max(UIUtils.S(8), tooltipX);

            float tooltipY = location.Y - tooltipH - UIUtils.S(12);
            if (tooltipY < UIUtils.S(8))
                tooltipY = location.Y + UIUtils.S(12);
            if (tooltipY + tooltipH > Height - UIUtils.S(8))
                tooltipY = Height - tooltipH - UIUtils.S(8);

            var tooltipRect = new RectangleF(tooltipX, tooltipY, tooltipW, tooltipH);
            using (var tooltipBrush = new SolidBrush(UIColors.MainBg))
                graphics.FillRectangle(tooltipBrush, tooltipRect);
            using (var borderPen = new Pen(UIColors.Border))
                graphics.DrawRectangle(borderPen, Rectangle.Round(tooltipRect));

            using var textBrush = new SolidBrush(_textColor);
            using var valueBrush = new SolidBrush(GetPnlColor(value));
            graphics.DrawString(dateText, titleFont, textBrush, tooltipX + paddingX, tooltipY + paddingY);
            graphics.DrawString(valueText, valueFont, valueBrush, tooltipX + paddingX, tooltipY + paddingY + dateSize.Height + UIUtils.S(2));
            graphics.DrawString(percentText, valueFont, valueBrush, tooltipX + paddingX, tooltipY + paddingY + dateSize.Height + valueSize.Height + UIUtils.S(4));
            graphics.DrawString(statusText, valueFont, valueBrush, tooltipX + paddingX, tooltipY + paddingY + dateSize.Height + valueSize.Height + percentSize.Height + UIUtils.S(6));
        }

        private void DrawEmptyState(Graphics graphics, List<YouPinDailyPnl> sortedPoints, int marginLeft, int marginRight, int marginTop, int marginBottom, int usableW, int usableH)
        {
            _hitPoints.Clear();
            _plotBounds = RectangleF.Empty;
            DrawGrid(graphics, marginLeft, marginRight, marginTop, usableH, -1, 1);

            using var brush = new SolidBrush(_subTextColor);
            using var font = new Font("Microsoft YaHei UI", _fontSize);
            string text = sortedPoints.Count == 1 ? "继续读取后形成曲线" : "完成多次读取后显示收益曲线";
            var size = graphics.MeasureString(text, font);
            graphics.DrawString(text, font, brush, marginLeft + (usableW - size.Width) / 2, marginTop + (usableH - size.Height) / 2);

            if (sortedPoints.Count == 1)
            {
                var point = sortedPoints[0];
                var location = new PointF(marginLeft + usableW / 2f, marginTop + usableH / 2f);
                UpdateHitPoints(sortedPoints, new[] { location }, marginLeft, marginTop, usableW, usableH);
                using (var pointBrush = new SolidBrush(_curveColor))
                    graphics.FillEllipse(pointBrush, location.X - 3f, location.Y - 3f, 6f, 6f);
                DrawSinglePointLabels(graphics, point, marginLeft, marginRight, marginTop, marginBottom, usableH);
                DrawHoverIndicator(graphics, marginTop, usableH);
            }
        }

        private void DrawGrid(Graphics graphics, int marginLeft, int marginRight, int marginTop, int usableH, double minVal, double maxVal)
        {
            float xRight = Width - marginRight;
            float yMid = marginTop + usableH / 2f;

            using var gridPen = new Pen(Color.FromArgb(UIColors.IsDark ? 80 : 95, UIColors.Border));
            gridPen.DashPattern = new float[] { 4, 4 };
            graphics.DrawLine(gridPen, marginLeft, yMid, xRight, yMid);

            if (minVal < 0 && maxVal > 0)
            {
                float zeroY = MapY(0, minVal, maxVal, marginTop, usableH);
                if (Math.Abs(zeroY - yMid) > UIUtils.S(4))
                {
                    using var zeroPen = new Pen(Color.FromArgb(UIColors.IsDark ? 115 : 130, _subTextColor));
                    graphics.DrawLine(zeroPen, marginLeft, zeroY, xRight, zeroY);
                }
            }
        }

        private void DrawCurveFill(Graphics graphics, PointF[] mapped, int marginTop, int usableH)
        {
            if (mapped.Length < 2)
                return;

            using var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddLine(mapped[0].X, marginTop + usableH, mapped[0].X, mapped[0].Y);
            for (int i = 1; i < mapped.Length; i++)
                path.AddLine(mapped[i - 1].X, mapped[i - 1].Y, mapped[i].X, mapped[i].Y);
            path.AddLine(mapped[^1].X, mapped[^1].Y, mapped[^1].X, marginTop + usableH);
            path.CloseFigure();

            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new PointF(0, marginTop),
                new PointF(0, marginTop + usableH),
                Color.FromArgb(34, _curveColor),
                Color.FromArgb(0, _curveColor));
            graphics.FillPath(brush, path);
        }

        private void DrawEndPoint(Graphics graphics, PointF lastPoint)
        {
            using (var brush = new SolidBrush(_curveColor))
                graphics.FillEllipse(brush, lastPoint.X - 3f, lastPoint.Y - 3f, 6f, 6f);

            using var ringPen = new Pen(UIColors.CardBg, 1.5f);
            graphics.DrawEllipse(ringPen, lastPoint.X - 3.5f, lastPoint.Y - 3.5f, 7f, 7f);
        }

        private void DrawLabels(Graphics graphics, List<YouPinDailyPnl> sortedPoints, double minVal, double maxVal, int marginLeft, int marginRight, int marginTop, int marginBottom, int usableW, int usableH)
        {
            var lastPoint = sortedPoints[^1];
            using var textBrush = new SolidBrush(_textColor);
            using var subBrush = new SolidBrush(_subTextColor);
            using var valueFont = new Font("Microsoft YaHei UI", _fontSize, FontStyle.Bold);
            using var subFont = new Font("Microsoft YaHei UI", Math.Max(7f, _fontSize - 1f));

            string lastValue = $"当前: {FormatCurveMoney(GetCurveValue(lastPoint))}";
            var lastValueSize = graphics.MeasureString(lastValue, valueFont);
            graphics.DrawString(lastValue, valueFont, textBrush, Width - marginRight - lastValueSize.Width, UIUtils.S(10));

            DrawAxisLabel(graphics, subFont, subBrush, FormatAxisMoney(maxVal), marginLeft - UIUtils.S(6), marginTop, alignRight: true);
            DrawAxisLabel(graphics, subFont, subBrush, FormatAxisMoney((maxVal + minVal) / 2d), marginLeft - UIUtils.S(6), marginTop + usableH / 2f, alignRight: true);
            DrawAxisLabel(graphics, subFont, subBrush, FormatAxisMoney(minVal), marginLeft - UIUtils.S(6), marginTop + usableH, alignRight: true);

            string firstDate = FormatCurveDate(sortedPoints[0].Date);
            string lastDate = FormatCurveDate(lastPoint.Date);
            graphics.DrawString(firstDate, subFont, subBrush, marginLeft, Height - marginBottom + UIUtils.S(8));

            var lastDateSize = graphics.MeasureString(lastDate, subFont);
            graphics.DrawString(lastDate, subFont, subBrush, Width - marginRight - lastDateSize.Width, Height - marginBottom + UIUtils.S(8));

            if (sortedPoints.Count > 2 && usableW > UIUtils.S(180))
            {
                var midPoint = sortedPoints[sortedPoints.Count / 2];
                string midDate = FormatCurveDate(midPoint.Date);
                var midDateSize = graphics.MeasureString(midDate, subFont);
                graphics.DrawString(midDate, subFont, subBrush, marginLeft + (usableW - midDateSize.Width) / 2f, Height - marginBottom + UIUtils.S(8));
            }
        }

        private void DrawSinglePointLabels(Graphics graphics, YouPinDailyPnl point, int marginLeft, int marginRight, int marginTop, int marginBottom, int usableH)
        {
            double value = GetCurveValue(point);
            using var textBrush = new SolidBrush(_textColor);
            using var subBrush = new SolidBrush(_subTextColor);
            using var valueFont = new Font("Microsoft YaHei UI", _fontSize, FontStyle.Bold);
            using var subFont = new Font("Microsoft YaHei UI", Math.Max(7f, _fontSize - 1f));

            string valueText = $"当前: {FormatCurveMoney(value)}";
            var valueSize = graphics.MeasureString(valueText, valueFont);
            graphics.DrawString(valueText, valueFont, textBrush, Width - marginRight - valueSize.Width, UIUtils.S(10));
            DrawAxisLabel(graphics, subFont, subBrush, FormatAxisMoney(value), marginLeft - UIUtils.S(6), marginTop + usableH / 2f, alignRight: true);
            graphics.DrawString(FormatCurveDate(point.Date), subFont, subBrush, marginLeft, Height - marginBottom + UIUtils.S(8));
        }

        private static double GetCurveValue(YouPinDailyPnl point) => point.ProfitAndLoss;

        private static double GetCurvePercent(YouPinDailyPnl point)
        {
            if (!double.IsNaN(point.ProfitAndLossPercent)
                && !double.IsInfinity(point.ProfitAndLossPercent)
                && Math.Abs(point.ProfitAndLossPercent) > 0.001)
            {
                return point.ProfitAndLossPercent;
            }

            double baseValue = point.EndValue - point.ProfitAndLoss;
            if (Math.Abs(baseValue) > 0.001)
                return point.ProfitAndLoss / baseValue * 100d;

            if (point.EndValue > 0.001)
                return point.ProfitAndLoss / point.EndValue * 100d;

            return 0d;
        }

        private static float MapY(double value, double minVal, double maxVal, int marginTop, int usableH)
        {
            if (Math.Abs(maxVal - minVal) < 0.001)
                return marginTop + usableH / 2f;

            return marginTop + (float)((maxVal - value) / (maxVal - minVal)) * usableH;
        }

        private static string FormatCurveMoney(double value)
        {
            string sign = value > 0 ? "+" : string.Empty;
            return $"{sign}¥{value:F2}";
        }

        private static string FormatCurvePercent(double value)
        {
            string sign = value > 0 ? "+" : string.Empty;
            return $"{sign}{value:F2}%";
        }

        private Color GetPnlColor(double value)
        {
            if (value > 0.001) return _riseColor;
            if (value < -0.001) return _fallColor;
            return _subTextColor;
        }

        private static string FormatPnlStatus(double value)
        {
            if (value > 0.001) return "盈利";
            if (value < -0.001) return "亏损";
            return "持平";
        }

        private static string FormatAxisMoney(double value)
        {
            double abs = Math.Abs(value);
            string sign = value > 0 ? "+" : value < 0 ? "-" : string.Empty;
            if (abs >= 10000)
                return $"{sign}{abs / 10000d:0.#}万";
            if (abs >= 1000)
                return $"{sign}{abs / 1000d:0.#}千";
            return $"{sign}{abs:0}";
        }

        private static string FormatCurveDate(string date)
        {
            if (DateTime.TryParse(date, out var parsed))
                return parsed.ToString("MM-dd");

            return string.IsNullOrWhiteSpace(date) ? "--" : date;
        }

        private static string FormatCurveFullDate(string date)
        {
            if (DateTime.TryParse(date, out var parsed))
                return parsed.ToString("yyyy-MM-dd");

            return string.IsNullOrWhiteSpace(date) ? "--" : date;
        }

        private static void DrawAxisLabel(Graphics graphics, Font font, Brush brush, string text, float rightX, float centerY, bool alignRight)
        {
            var size = graphics.MeasureString(text, font);
            float x = alignRight ? rightX - size.Width : rightX;
            graphics.DrawString(text, font, brush, x, centerY - size.Height / 2f);
        }
    }


}
