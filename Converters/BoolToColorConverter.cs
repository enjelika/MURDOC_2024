using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MURDOC_2024.Converters
{
    /// <summary>Converts a bool to a background brush: light green when active, light gray when inactive.</summary>
    public class BoolToColorConverter : IValueConverter
    {
        /// <summary>Returns a light green brush when <paramref name="value"/> is <see langword="true"/>, gray otherwise.</summary>
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

        /// <summary>Not supported — this converter is one-way only.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}