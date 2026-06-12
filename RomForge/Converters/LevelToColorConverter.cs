using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RomForge.Converters;

public class LevelToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double level)
        {
            if (level <= 2) return Brushes.White;

            double t = (level - 3) / 15.0;
            t = Math.Clamp(t, 0, 1);

            byte r = (byte)(255 * t);
            byte g = 255;
            byte b = (byte)(255 * (1.0 - t * 0.8));

            return new SolidColorBrush(Color.FromArgb(255, r, g, b));
        }
        return Brushes.White;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}