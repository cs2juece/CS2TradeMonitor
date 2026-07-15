using CS2TradeMonitor.Domain.Steam;
using CS2TradeMonitor.src.Core;
using CS2TradeMonitor.src.UI.Controls;
using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace CS2TradeMonitor.src.UI.Framework.SteamOffers
{
    internal static class SteamOfferImageProvider
    {
        private static readonly RemoteImageCache OfferImages = RemoteImageCache.CreateDomestic(
            timeoutSeconds: 10,
            maxBytes: 2 * 1024 * 1024,
            failureTtl: TimeSpan.FromMinutes(30));
        private static readonly Lazy<Image> DarkOfferImagePlaceholder = new(() => CreateOfferImagePlaceholder(dark: true));
        private static readonly Lazy<Image> LightOfferImagePlaceholder = new(() => CreateOfferImagePlaceholder(dark: false));

        public static string PickOfferIconUrl(SteamOfferItem offer)
        {
            foreach (var asset in offer.ItemsToReceive.Concat(offer.ItemsToGive))
            {
                if (!string.IsNullOrWhiteSpace(asset.IconUrl))
                    return asset.IconUrl.Trim();
            }

            return "";
        }

        public static bool TryGet(string imageUrl, out Image? image)
        {
            return OfferImages.TryGet(imageUrl, out image);
        }

        public static Task<Image?> GetAsync(string imageUrl)
        {
            return OfferImages.GetAsync(imageUrl);
        }

        public static Image GetPlaceholder()
        {
            return UIColors.IsDark ? DarkOfferImagePlaceholder.Value : LightOfferImagePlaceholder.Value;
        }

        private static Image CreateOfferImagePlaceholder(bool dark)
        {
            var bitmap = new Bitmap(96, 96);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(dark ? Color.FromArgb(25, 31, 38) : Color.FromArgb(245, 247, 250));

            var card = new Rectangle(14, 16, 68, 64);
            using (var fill = new SolidBrush(dark ? Color.FromArgb(35, 43, 52) : Color.FromArgb(235, 239, 244)))
                graphics.FillRectangle(fill, card);
            using (var border = new Pen(dark ? Color.FromArgb(70, 82, 98) : Color.FromArgb(190, 199, 210), 2f))
                graphics.DrawRectangle(border, card);

            using (var accent = new Pen(dark ? UIColors.Primary : Color.FromArgb(0, 120, 215), 4f))
            {
                graphics.DrawLine(accent, 28, 58, 44, 44);
                graphics.DrawLine(accent, 44, 44, 56, 54);
                graphics.DrawLine(accent, 56, 54, 70, 36);
            }

            using var brush = new SolidBrush(dark ? Color.FromArgb(145, 158, 174) : Color.FromArgb(108, 118, 132));
            using var font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            var textSize = graphics.MeasureString("无图", font);
            graphics.DrawString("无图", font, brush, (bitmap.Width - textSize.Width) / 2f, 68);
            return bitmap;
        }
    }
}
