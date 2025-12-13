using System;
using System.Globalization;
using System.Windows.Data;

// Value converters for XAML binding
namespace Vortex.UI.Converters
{
    // Checks if value contains unknown text
    public class ContainsUnknownConverter : IValueConverter
    {
        // Converts value to unknown check result
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return false;

            string text = value.ToString();
            return !string.IsNullOrEmpty(text) && 
                   text.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Not implemented for one-way binding
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
