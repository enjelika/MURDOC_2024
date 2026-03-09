using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MURDOC_2024.Converters
{
    /// <summary>Converts a bool to <see cref="Visibility"/>: Visible when true, Collapsed when false.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        /// <summary>Returns <see cref="Visibility.Visible"/> when <paramref name="value"/> is <see langword="true"/>.</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is <see cref="Visibility.Visible"/>.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility == Visibility.Visible;
            }
            return false;
        }
    }

    /// <summary>
    /// Inverse converter — returns Visible when false
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        /// <summary>Returns <see cref="Visibility.Visible"/> when <paramref name="value"/> is <see langword="false"/>.</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Collapsed : Visibility.Visible;
            }
            return Visibility.Visible;
        }

        /// <summary>Returns <see langword="true"/> when <paramref name="value"/> is not <see cref="Visibility.Visible"/>.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                return visibility != Visibility.Visible;
            }
            return true;
        }
    }
}