using System;
using System.Globalization;
using System.Windows.Data;

namespace MURDOC_2024.Converters
{
    /// <summary>Converts a bool to a checkmark (✓) or open-circle (○) string for UI display.</summary>
    public class BoolToCheckmarkConverter : IValueConverter
    {
        /// <summary>Returns "✓" when <paramref name="value"/> is <see langword="true"/>, "○" otherwise.</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? "✓" : "○";
            }
            return "○";
        }

        /// <summary>Not supported — this converter is one-way only.</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}