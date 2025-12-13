using System.Windows;
using Vortex.UI.ViewModels;

namespace Vortex.UI
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // Navigate to Dashboard page by default
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetFrame(MainFrame);
                vm.NavigateToDashboard();
            }
        }
    }
}