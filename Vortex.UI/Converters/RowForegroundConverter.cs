using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

// Value converters for XAML binding
namespace Vortex.UI.Converters
{
    // Converts row data to foreground color
    public class RowForegroundConverter : IMultiValueConverter
    {
        // Cached white brush for default
        private static readonly SolidColorBrush WhiteBrush = new SolidColorBrush(Colors.White);
        // Cached red brush for deleted
        private static readonly SolidColorBrush RedBrush = new SolidColorBrush(Colors.Red);
        // Cached goldenrod brush for unknown
        private static readonly SolidColorBrush DarkGoldenrodBrush = new SolidColorBrush(Color.FromRgb(184, 134, 11));

        // Converts colored flag and status to brush
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return WhiteBrush;

            if (values[0] is bool coloredResults && !coloredResults)
                return WhiteBrush;

            string modified = values[1]?.ToString() ?? string.Empty;

            if (!string.IsNullOrEmpty(modified) && 
                modified.Equals("Deleted", StringComparison.OrdinalIgnoreCase))
            {
                return RedBrush;
            }

            if (!string.IsNullOrEmpty(modified) && 
                modified.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return DarkGoldenrodBrush;
            }

            return WhiteBrush;
        }

        // Not implemented for one-way binding
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException("RowForegroundConverter does not support two-way binding");
        }
    }
}
