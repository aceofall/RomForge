using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;

namespace NSW.WPF.Services;

public static class ByteArrayExtensions
{
    public static BitmapImage? ToBitmapImage(this byte[] imageData)
    {
        if (imageData == null || imageData.Length == 0)
            return null;

        var image = new BitmapImage();
        using var memoryStream = new MemoryStream(imageData);

        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memoryStream;
        image.EndInit();
        image.Freeze();

        return image;
    }

    public static byte[] ResizePng(this byte[] imageBytes, int width, int height)
    {
        using var input = new MemoryStream(imageBytes);
        using var img = Image.FromStream(input);

        using var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);

        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        g.DrawImage(img, 0, 0, width, height);

        using var output = new MemoryStream();
        bmp.Save(output, ImageFormat.Png);

        return output.ToArray();
    }
}