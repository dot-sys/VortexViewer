using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Vortex.UI.ViewModels;
using Vortex.UI.Helpers;

// UI views for application pages
namespace Vortex.UI.Views
{
    // Timeline view for registry artifacts
    public partial class TimelineView : Page
    {
        // Gets associated view model instance
        private TimelineViewModel ViewModel => DataContext as TimelineViewModel;

        // Initializes timeline view components
        public TimelineView()
        {
            InitializeComponent();
            
            this.Loaded += TimelineView_Loaded;
        }

        // Enables timestamp mask on load
        private void TimelineView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            Vortex.UI.Controls.TimestampMask.SetIsEnabled(TimestampFilter, true);
        }

        // Configures context menu visibility
        private void TimelineDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            if (dataGrid != null)
            {
                bool isPathColumn = DataGridContextMenuHelper.IsPathColumn(dataGrid);
                GoToMenuItem.Visibility = isPathColumn ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Copies selected value to clipboard
        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            DataGridContextMenuHelper.CopyValue(TimelineDataGrid);
        }

        // Copies entire row to clipboard
        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            DataGridContextMenuHelper.CopyRow(TimelineDataGrid);
        }

        // Opens path in file explorer
        private void GoTo_Click(object sender, RoutedEventArgs e)
        {
            DataGridContextMenuHelper.GoToPath(TimelineDataGrid);
        }

        // Updates text on description dropdown open
        private void DescriptionFilter_DropDownOpened(object sender, EventArgs e)
        {
            UpdateComboBoxText(DescriptionFilter, ViewModel.AvailableDescriptions.Where(x => x.IsSelected).Select(x => x.Description), "Filter by Description...");
        }

        // Updates text on source dropdown open
        private void SourceFilter_DropDownOpened(object sender, EventArgs e)
        {
            UpdateComboBoxText(SourceFilter, ViewModel.AvailableSources.Where(x => x.IsSelected).Select(x => x.Source), "Filter by Source...");
        }

        // Updates text on description dropdown close
        private void DescriptionFilter_DropDownClosed(object sender, EventArgs e)
        {
            UpdateComboBoxText(DescriptionFilter, ViewModel.AvailableDescriptions.Where(x => x.IsSelected).Select(x => x.Description), "Filter by Description...");
        }

        // Updates text on source dropdown close
        private void SourceFilter_DropDownClosed(object sender, EventArgs e)
        {
            UpdateComboBoxText(SourceFilter, ViewModel.AvailableSources.Where(x => x.IsSelected).Select(x => x.Source), "Filter by Source...");
        }

        // Updates combo box display text
        private void UpdateComboBoxText(ComboBox comboBox, System.Collections.Generic.IEnumerable<string> selectedItems, string placeholder)
        {
            var items = selectedItems.ToList();
            if (items.Any())
            {
                comboBox.Text = string.Join(", ", items);
            }
            else
            {
                comboBox.Text = string.Empty;
            }
            
            if (ViewModel != null)
            {
                if (comboBox == DescriptionFilter)
                    ViewModel.DescriptionFilterText = comboBox.Text;
                else if (comboBox == SourceFilter)
                    ViewModel.SourceFilterText = comboBox.Text;
            }
        }

        // Handles description filter selection changes
        private void DescriptionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DescriptionFilter.SelectedItem != null)
                DescriptionFilter.SelectedItem = null;
            
            UpdateComboBoxText(DescriptionFilter, ViewModel?.AvailableDescriptions?.Where(x => x.IsSelected).Select(x => x.Description) ?? System.Linq.Enumerable.Empty<string>(), "Filter by Description...");
        }

        // Handles source filter selection changes
        private void SourceFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SourceFilter.SelectedItem != null)
                SourceFilter.SelectedItem = null;
            
            UpdateComboBoxText(SourceFilter, ViewModel?.AvailableSources?.Where(x => x.IsSelected).Select(x => x.Source) ?? System.Linq.Enumerable.Empty<string>(), "Filter by Source...");
        }
    }
}
