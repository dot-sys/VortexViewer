using System;
using System.Globalization;
using System.Windows.Data;

// Value converters for XAML binding
namespace Vortex.UI.Converters
{
    // Checks if value contains deleted text
    public class ContainsDeletedConverter : IValueConverter
    {
        // Converts value to deleted check result
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return false;

            string text = value.ToString();
            return !string.IsNullOrEmpty(text) && 
                   text.IndexOf("Deleted", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Not implemented for one-way binding
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
