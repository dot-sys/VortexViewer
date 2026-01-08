using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Vortex.UI.ViewModels;

// UI views for application pages
namespace Vortex.UI.Views
{
    // Welcome screen managing trace initialization
    public partial class WelcomeView : Page
    {
        // Reference to main window view model
        private MainWindowViewModel _viewModel;
        // Tracks if trace already started
        private bool _isTraceStarted = false;
        // Timer for animated dots display
        private DispatcherTimer _dotsTimer;
        // Counter for animated dots
        private int _dotsCount = 0;
        // Storyboard for logo spinning animation
        private Storyboard _logoSpinStoryboard;
        
        // Initializes welcome view and timer
        public WelcomeView()
        {
            InitializeComponent();
            
            _dotsTimer = new DispatcherTimer();
            _dotsTimer.Interval = TimeSpan.FromMilliseconds(500);
            _dotsTimer.Tick += DotsTimer_Tick;
        }
        
        // Updates animated dots display
        private void DotsTimer_Tick(object sender, EventArgs e)
        {
            _dotsCount = (_dotsCount + 1) % 4;
            DotsText.Text = new string('.', _dotsCount);
        }

        // Handles trace initialization button click
        private async void StartTrace_Click(object sender, RoutedEventArgs e)
        {
            if (_isTraceStarted) return;
            
            _isTraceStarted = true;
            _viewModel = DataContext as MainWindowViewModel;
            
            if (_viewModel == null)
            {
                MessageBox.Show("Unable to access MainWindowViewModel", "Error");
                return;
            }

            StartTraceButton.IsEnabled = false;
            StatusPanel.Visibility = Visibility.Visible;
            StatusText.Text = "Starting traces";

            StartLogoSpin();

            _viewModel.StartAllTraces();

            await Task.Delay(1500);

            await FadeOutStatusText();

            StatusText.Text = "Parsing: Journal, Drives, Timeline";
            await FadeInStatusText();

            _dotsCount = 0;
            DotsText.Text = "";
            _dotsTimer.Start();

            await MonitorTracesAsync();
        }

        // Monitors trace completion asynchronously
        private async Task MonitorTracesAsync()
        {
            while (true)
            {
                await Task.Delay(500);

                bool journalComplete = false;
                bool drivesComplete = false;
                bool timelineComplete = false;

                if (_viewModel.JournalViewModel != null)
                {
                    journalComplete = !_viewModel.JournalViewModel.IsLoading;
                }

                if (_viewModel.DrivesViewModel != null)
                {
                    drivesComplete = !_viewModel.DrivesViewModel.IsLoading;
                }

                if (_viewModel.TimelineViewModel != null)
                {
                    timelineComplete = !_viewModel.TimelineViewModel.IsLoading;
                }

                var statusParts = new System.Collections.Generic.List<string>();
                if (!journalComplete) statusParts.Add("Journal");
                if (!drivesComplete) statusParts.Add("Drives");
                if (!timelineComplete) statusParts.Add("Timeline");

                if (statusParts.Count > 0)
                {
                    StatusText.Text = $"Parsing: {string.Join(", ", statusParts)}";
                }
                else
                {
                    _dotsTimer.Stop();
                    StatusText.Text = "All traces complete! Cleaning up memory...";
                    DotsText.Text = "";
                    
                    StopLogoSpin();
                    
                    _viewModel.IsDataLoaded = true;
                    
                    // Force garbage collection after all results are collected
                    // At this point, all intermediate data structures have been converted to final results
                    await Task.Run(() =>
                    {
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    });
                    
                    StatusText.Text = "All traces complete!";
                    
                    break;
                }
            }

            await Task.Delay(500);
            await FadeOutAndNavigate();
        }

        // Fades out status text animation
        private async Task FadeOutStatusText()
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var tcs = new TaskCompletionSource<bool>();
            fadeOut.Completed += (s, e) => tcs.SetResult(true);
            StatusPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            await tcs.Task;
        }

        // Fades in status text animation
        private async Task FadeInStatusText()
        {
            var fadeIn = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            var tcs = new TaskCompletionSource<bool>();
            fadeIn.Completed += (s, e) => tcs.SetResult(true);
            StatusPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            await tcs.Task;
        }

        // Fades out and navigates to dashboard
        private async Task FadeOutAndNavigate()
        {
            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            var tcs = new TaskCompletionSource<bool>();
            fadeOut.Completed += (s, e) => tcs.SetResult(true);
            MainGrid.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            await tcs.Task;

            _viewModel?.NavigateToDashboard();
        }

        // Starts logo spinning animation
        private void StartLogoSpin()
        {
            if (_logoSpinStoryboard != null)
                return;

            var logoImage = (Image)this.FindName("WelcomeLogoImage");
            if (logoImage == null)
                return;

            var storyboard = new Storyboard();
            storyboard.RepeatBehavior = RepeatBehavior.Forever;

            var rotationAnimation = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromSeconds(1.2),
                RepeatBehavior = RepeatBehavior.Forever
            };

            rotationAnimation.EasingFunction = new PowerEase
            {
                EasingMode = EasingMode.EaseInOut,
                Power = 2
            };

            Storyboard.SetTarget(rotationAnimation, logoImage);
            Storyboard.SetTargetProperty(rotationAnimation, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
            storyboard.Children.Add(rotationAnimation);

            _logoSpinStoryboard = storyboard;
            _logoSpinStoryboard.Begin();
        }

        // Stops logo spinning animation smoothly
        private void StopLogoSpin()
        {
            if (_logoSpinStoryboard == null)
                return;

            var logoImage = (Image)this.FindName("WelcomeLogoImage");
            if (logoImage == null)
                return;

            _logoSpinStoryboard.Stop();
            
            var resetAnimation = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromSeconds(0.5),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            
            var rotateTransform = logoImage.RenderTransform as RotateTransform;
            if (rotateTransform != null)
            {
                rotateTransform.BeginAnimation(RotateTransform.AngleProperty, resetAnimation);
            }
            _logoSpinStoryboard = null;
        }
    }
}


