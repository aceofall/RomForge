using System.Globalization;
using System.Windows.Data;

namespace RomForge.Converters
{
    public class ProgressToStarConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            double progress = 0;

            if (value is double d)
                progress = d;
            else if (value is int i) 
                progress = i;
            else if (value is float f) 
                progress = f;

            if (progress <= 0) 
                return 0.0001;

            if (progress >= 100) 
                return 1.0;

            return progress / 100.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}