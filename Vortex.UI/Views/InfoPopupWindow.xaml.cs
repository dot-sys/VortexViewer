using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

// UI views for application pages
namespace Vortex.UI.Views
{
    // Popup window for information display
    public partial class InfoPopupWindow : Window
    {
        // Default constructor for popup window
        public InfoPopupWindow()
        {
            InitializeComponent();
        }

        // Creates popup with text content
        public InfoPopupWindow(string title, string content) : this()
        {
            Title = title;
            
            var textBlock = new TextBlock
            {
                Text = content,
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(5)
            };
            
            ContentPresenter.Content = textBlock;
        }

        // Creates popup with UI element
        public InfoPopupWindow(string title, UIElement content) : this()
        {
            Title = title;
            ContentPresenter.Content = content;
        }

        // Closes popup window on click
        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // Enables window dragging from titlebar
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    this.DragMove();
                }
                catch
                {
                }
            }
        }
    }
}
