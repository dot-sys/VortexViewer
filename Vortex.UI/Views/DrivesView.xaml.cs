using System;
using System.Windows;
using System.Windows.Controls;
using Vortex.UI.ViewModels;
using Vortex.UI.Helpers;

// UI views for application pages
namespace Vortex.UI.Views
{
    // Drives view for USB forensics
    public partial class DrivesView : Page
    {
        // Initializes drives view components
        public DrivesView()
        {
            try
            {
                InitializeComponent();
            }
            catch (Exception)
            {    
             throw;
            }
        }

        // Copies selected cell value
        private void CopyValue_Click(object sender, RoutedEventArgs e)
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
        private void CopyRow_Click(object sender, RoutedEventArgs e)
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
