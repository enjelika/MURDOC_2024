using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MURDOC_2024.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isActive && isActive)
            {
                // Active color - light green
                return new SolidColorBrush(Color.FromRgb(200, 230, 201));
            }

            // Inactive color - default button color
            return new SolidColorBrush(Color.FromRgb(240, 240, 240));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}