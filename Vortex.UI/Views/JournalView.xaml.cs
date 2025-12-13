using System;
using System.Windows;
using System.Windows.Controls;
using Vortex.UI.ViewModels;
using Vortex.UI.Helpers;

// UI views for application pages
namespace Vortex.UI.Views
{
    // Journal view displaying USN entries
    public partial class JournalView : Page
    {
        // Initializes journal view components
        public JournalView()
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

        // Shows context menu with path actions
        private void JournalDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid != null)
            {
                bool isPathColumn = DataGridContextMenuHelper.IsPathColumn(dataGrid);
                GoToMenuItem.Visibility = isPathColumn ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Copies selected cell value to clipboard
        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            DataGridContextMenuHelper.CopyValue(JournalDataGrid);
        }

        // Copies entire row to clipboard
        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            DataGridContextMenuHelper.CopyRow(JournalDataGrid);
        }

        // Opens file path in explorer
        private void GoTo_Click(object sender, RoutedEventArgs e)
        {
            DataGridContextMenuHelper.GoToPath(JournalDataGrid);
        }
    }
}