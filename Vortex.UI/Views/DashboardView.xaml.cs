using System.Windows.Controls;
using Vortex.UI.ViewModels;
using Vortex.UI.Helpers;

// UI views for application pages
namespace Vortex.UI.Views
{
    // Dashboard view for system overview
    public partial class DashboardView : Page
    {
        // Initializes dashboard view components
        public DashboardView()
        {
            InitializeComponent();
        }

        // Copies selected cell value
        private void CopyValue_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var dataGrid = contextMenu?.PlacementTarget as DataGrid;

            if (dataGrid != null)
            {
                DataGridContextMenuHelper.CopyValue(dataGrid);
            }
        }

        // Copies entire selected row
        private void CopyRow_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var dataGrid = contextMenu?.PlacementTarget as DataGrid;

            if (dataGrid != null)
            {
                DataGridContextMenuHelper.CopyRow(dataGrid);
            }
        }
    }
}