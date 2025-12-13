using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Vortex.UI.ViewModels;
using Vortex.UI.Helpers;
using System.Windows.Threading;
using System.ComponentModel;

// UI views for application pages
namespace Vortex.UI.Views
{
    // Processes view for memory analysis
    public partial class ProcessesView : Page
    {
        // Timer for loading animation dots
        private DispatcherTimer _dotsTimer;
        // Counter for animated dots
        private int _dotsCount = 0;

        // Initializes view and sets up timer
        public ProcessesView()
        {
            InitializeComponent();
            var viewModel = new ProcessesViewModel();
            DataContext = viewModel;

            _dotsTimer = new DispatcherTimer();
            _dotsTimer.Interval = System.TimeSpan.FromMilliseconds(500);
            _dotsTimer.Tick += DotsTimer_Tick;

            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        // Handles loading state property changes
        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProcessesViewModel.IsLoadingStrings) || 
                e.PropertyName == nameof(ProcessesViewModel.IsLoadingPCA))
            {
                var viewModel = DataContext as ProcessesViewModel;
                if (viewModel != null)
                {
                    if (viewModel.IsLoadingStrings || viewModel.IsLoadingPCA)
                    {
                        _dotsCount = 0;
                        DotsTextBlock.Text = "";
                        _dotsTimer.Start();
                    }
                    else
                    {
                        _dotsTimer.Stop();
                        DotsTextBlock.Text = "";
                    }
                }
            }
        }

        // Updates animated dots for loading
        private void DotsTimer_Tick(object sender, System.EventArgs e)
        {
            _dotsCount = (_dotsCount + 1) % 4;
            DotsTextBlock.Text = new string('.', _dotsCount);
        }

        // Opens combo box on focus
        private void ProcessComboBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo != null)
                combo.IsDropDownOpen = true;
        }

        // Handles combo box click behavior
        private void ProcessComboBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo != null && !combo.IsDropDownOpen)
            {
                combo.IsDropDownOpen = true;
                e.Handled = true;
            }
        }

        // Copies selected value to clipboard
        private void CopyValue_Click(object sender, RoutedEventArgs e)
        {
            DataGridContextMenuHelper.CopyValue(StringsDataGrid);
        }

        // Copies selected row to clipboard
        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            DataGridContextMenuHelper.CopyRow(StringsDataGrid);
        }
    }
}