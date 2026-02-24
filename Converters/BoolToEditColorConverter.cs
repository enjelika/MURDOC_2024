using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MURDOC_2024.Converters
{
    public class BoolToEditColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            }
            return new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}