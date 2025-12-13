using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections;

// UI views for application pages
namespace Vortex.UI.Views
{
    // Helper for displaying popup windows
    public static class PopupHelper
    {
        // Shows text content in popup window
        public static void ShowTextPopup(string title, string content, Window owner = null)
        {
            var popup = new InfoPopupWindow(title, content);
            popup.Owner = owner ?? Application.Current.MainWindow;
            popup.ShowDialog();
        }

        // Shows datagrid content in popup window
        public static void ShowDataGridPopup(string title, IEnumerable data, Window owner = null)
        {
            var dataGrid = new DataGrid
            {
                ItemsSource = data,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                RowBackground = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(10, 10, 10)),
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(0),
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            try
            {
                var style = Application.Current.FindResource("VortexDataGridStyle") as Style;
                if (style != null)
                {
                    dataGrid.Style = style;
                }
            }
            catch
            {
            }

            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Path",
                Binding = new System.Windows.Data.Binding("Path"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
            
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Modified",
                Binding = new System.Windows.Data.Binding("Modified"),
                MaxWidth = 75,
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
            });
            
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Signed",
                Binding = new System.Windows.Data.Binding("Signed"),
                MaxWidth = 100,
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
            });
            
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Source",
                Binding = new System.Windows.Data.Binding("Source"),
                MaxWidth = 75,
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
            });

            var popup = new InfoPopupWindow(title, dataGrid);
            popup.Owner = owner ?? Application.Current.MainWindow;
            popup.SizeToContent = SizeToContent.WidthAndHeight;
            popup.ShowDialog();
        }
    }
}
