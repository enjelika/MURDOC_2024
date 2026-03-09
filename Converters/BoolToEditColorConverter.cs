using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MURDOC_2024.Converters
{
    /// <summary>Converts a bool to a brush indicating edit state: green when active, gray when inactive.</summary>
    public class BoolToEditColorConverter : IValueConverter
    {
        /// <summary>Returns a green brush when <paramref name="value"/> is <see langword="true"/>, gray otherwise.</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                return new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
            }
            return new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
        }

        /// <summary>Not supported — this converter is one-way only.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}