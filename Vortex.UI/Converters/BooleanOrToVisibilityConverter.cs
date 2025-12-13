using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

// Value converters for XAML binding
namespace Vortex.UI.Converters
{
    // Converts multiple booleans to visibility using OR
    public class BooleanOrToVisibilityConverter : IMultiValueConverter
    {
        // Converts boolean array to visibility
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || !values.Any())
                return Visibility.Collapsed;

            bool anyTrue = values.OfType<bool>().Any(b => b);
            return anyTrue ? Visibility.Visible : Visibility.Collapsed;
        }

        // Not implemented for one-way binding
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("BooleanOrToVisibilityConverter does not support two-way binding");
        }
    }
}
