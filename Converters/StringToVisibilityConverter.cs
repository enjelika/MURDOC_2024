using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MURDOC_2024.Converters
{
    /// <summary>Converts a string to <see cref="Visibility"/>: Visible when non-empty, Collapsed when null or empty.</summary>
    public class StringToVisibilityConverter : IValueConverter
    {
        /// <summary>Returns <see cref="Visibility.Visible"/> when the string is non-null and non-empty.</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Not supported — this converter is one-way only.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}