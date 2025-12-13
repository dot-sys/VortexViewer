using System;
using System.Globalization;
using System.Windows.Data;

// Value converters for XAML binding
namespace Vortex.UI.Converters
{
    // Inverts boolean values for binding
    [ValueConversion(typeof(bool), typeof(bool))]
    public class InverseBooleanConverter : IValueConverter
    {
        // Inverts boolean value
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }

        // Inverts boolean value back
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return !boolValue;
            }
            return value;
        }
    }
}