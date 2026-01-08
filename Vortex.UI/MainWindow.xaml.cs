using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Vortex.UI.ViewModels;

// WPF application entry point and configuration
namespace Vortex.UI
{
    // Main window managing navigation and controls
    public partial class MainWindow : Window
    {
        // Tracks first navigation for animation
        private bool _isFirstNavigation = true;

        // Initializes main window and event handlers
        public MainWindow()
        {
            InitializeComponent();
            
            this.Loaded += MainWindow_Loaded;
            
            // Wire up language menu item clicks
            foreach (MenuItem item in LanguageButton.ContextMenu.Items)
            {
                item.Click += LanguageMenuItem_Click;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.SetFrame(MainFrame);
            }
            
            CloseButton.ApplyTemplate();
        }

        // Handles window state changes for border radius
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            var mainBorder = this.Content as Border;
            
            if (WindowState == WindowState.Maximized)
            {
                if (mainBorder != null)
                {
                    mainBorder.CornerRadius = new CornerRadius(0);
                }
                
                UpdateCloseButtonCornerRadius(new CornerRadius(0));
            }
            else if (WindowState == WindowState.Normal)
            {
                if (mainBorder != null)
                {
                    mainBorder.CornerRadius = new CornerRadius(8);
                }
                
                UpdateCloseButtonCornerRadius(new CornerRadius(0, 8, 0, 0));
            }
        }

        // Updates close button corner radius dynamically
        private void UpdateCloseButtonCornerRadius(CornerRadius radius)
        {
            if (CloseButton != null)
            {
                CloseButton.ApplyTemplate();
                var border = CloseButton.Template?.FindName("CloseBorder", CloseButton) as Border;
                if (border != null)
                {
                    border.CornerRadius = radius;
                }
            }
        }

        // Handles titlebar drag and maximize
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximizeRestore();
            }
            else
            {
                try
                {
                    this.DragMove();
                }
                catch { }
            }
        }

        // Minimizes window to taskbar
        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        // Refreshes current view content
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                vm.RefreshCurrentView();
            }
        }

        // Opens language selection menu
        private void Language_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.ContextMenu != null)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                button.ContextMenu.IsOpen = true;
            }
        }

        // Toggles maximize restore window state
        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximizeRestore();
        }

        // Switches between maximized and normal states
        private void ToggleMaximizeRestore()
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowState = WindowState.Maximized;
            }
        }

        // Closes main window
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Animates content on navigation
        private void MainFrame_Navigated(object sender, NavigationEventArgs e)
        {
            var content = MainFrame.Content as FrameworkElement;
            if (content == null)
                return;

            if (_isFirstNavigation)
            {
                _isFirstNavigation = false;
                content.Opacity = 1;
                return;
            }

            content.Opacity = 0;

            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.8),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            content.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        // Handles language menu item selection
        private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Header is string languageCode)
            {
                ChangeLanguage(languageCode);
            }
        }

        // Changes the application language
        private void ChangeLanguage(string languageCode)
        {
            string resourcePath;
            
            switch (languageCode)
            {
                case "EN":
                    resourcePath = "Resources/Strings.en-US.xaml";
                    break;
                case "DE":
                    resourcePath = "Resources/Strings.de-DE.xaml";
                    break;
                case "RU":
                    resourcePath = "Resources/Strings.ru-RU.xaml";
                    break;
                case "ES":
                    resourcePath = "Resources/Strings.es-ES.xaml";
                    break;
                case "PT":
                    resourcePath = "Resources/Strings.pt-PT.xaml";
                    break;
                default:
                    resourcePath = "Resources/Strings.en-US.xaml";
                    break;
            }

            // Find and remove the existing language resource dictionary
            var existingLanguageDict = Application.Current.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source != null && 
                    (d.Source.OriginalString.Contains("Resources/Strings.") || 
                     d.Source.OriginalString.Contains("/Resources/Strings.")));
            
            if (existingLanguageDict != null)
            {
                Application.Current.Resources.MergedDictionaries.Remove(existingLanguageDict);
            }
            
            // Create and add the new language resource dictionary
            var newDict = new ResourceDictionary 
            { 
                Source = new Uri(resourcePath, UriKind.Relative) 
            };
            
            // Insert at the beginning to ensure it takes precedence
            Application.Current.Resources.MergedDictionaries.Insert(0, newDict);
            
            // Update language button text
            LanguageButton.Content = languageCode;
            
            // Reload the current view to apply the language change
            if (DataContext is MainWindowViewModel vm)
            {
                vm.ReloadCurrentView();
            }
        }
    }
}
