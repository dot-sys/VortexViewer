using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Input;
using Vortex.UI.Views;

// ViewModel layer for UI binding
namespace Vortex.UI.ViewModels
{
    // ViewModel managing main window navigation logic
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        // Reference to main navigation frame
        private Frame _mainFrame;
        // Instance of journal view model
        private JournalViewModel _journalViewModel;
        // Instance of drives view model
        private DrivesViewModel _drivesViewModel;
        // Instance of timeline view model
        private TimelineViewModel _timelineViewModel;
        // Instance of dashboard view model
        private DashboardViewModel _dashboardViewModel;

        // Backing field for data loaded state
        private bool _isDataLoaded = false;
        // Indicates if trace data loaded
        public bool IsDataLoaded
        {
            get => _isDataLoaded;
            set
            {
                if (_isDataLoaded != value)
                {
                    _isDataLoaded = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanNavigateToDashboard));
                    OnPropertyChanged(nameof(CanNavigateToJournal));
                    OnPropertyChanged(nameof(CanNavigateToTimeline));
                    OnPropertyChanged(nameof(CanNavigateToDrives));
                    OnPropertyChanged(nameof(CanNavigateToProcesses));
                }
            }
        }

        // Can navigate to dashboard view
        public bool CanNavigateToDashboard => IsDataLoaded;
        // Can navigate to journal view
        public bool CanNavigateToJournal => IsDataLoaded;
        // Can navigate to timeline view
        public bool CanNavigateToTimeline => IsDataLoaded;
        // Can navigate to drives view
        public bool CanNavigateToDrives => IsDataLoaded;
        // Can navigate to processes view
        public bool CanNavigateToProcesses => IsDataLoaded;

        // Initializes commands and navigation
        public MainWindowViewModel()
        {
            NavigateToDashboardCommand = new RelayCommand(NavigateToDashboard, () => CanNavigateToDashboard);
            NavigateToJournalCommand = new RelayCommand(NavigateToJournal, () => CanNavigateToJournal);
            NavigateToProcessesCommand = new RelayCommand(NavigateToProcesses, () => CanNavigateToProcesses);
            NavigateToPlaceholder3Command = new RelayCommand(NavigateToPlaceholder3);
            NavigateToTimelineCommand = new RelayCommand(NavigateToTimeline, () => CanNavigateToTimeline);
            NavigateToDrivesCommand = new RelayCommand(NavigateToDrives, () => CanNavigateToDrives);
        }

        // Command to navigate to dashboard
        public ICommand NavigateToDashboardCommand { get; }
        // Command to navigate to journal
        public ICommand NavigateToJournalCommand { get; }
        // Command to navigate to processes
        public ICommand NavigateToProcessesCommand { get; }
        // Command to navigate to timeline
        public ICommand NavigateToTimelineCommand { get; }
        // Placeholder navigation command
        public ICommand NavigateToPlaceholder3Command { get; }
        // Command to navigate to drives
        public ICommand NavigateToDrivesCommand { get; }
        // Placeholder navigation command
        public ICommand NavigateToPlaceholder4Command { get; }

        // Gets journal view model instance
        public JournalViewModel JournalViewModel => _journalViewModel;
        // Gets drives view model instance
        public DrivesViewModel DrivesViewModel => _drivesViewModel;
        // Gets timeline view model instance
        public TimelineViewModel TimelineViewModel => _timelineViewModel;
        // Gets dashboard view model instance
        public DashboardViewModel DashboardViewModel => _dashboardViewModel;

        // Sets navigation frame reference
        public void SetFrame(Frame frame)
        {
            _mainFrame = frame;
            NavigateToWelcome();
        }

        // Navigates to welcome screen
        public void NavigateToWelcome()
        {
            if (_mainFrame != null)
            {
                var welcomeView = new WelcomeView();
                welcomeView.DataContext = this;
                _mainFrame.Navigate(welcomeView);
            }
        }

        // Navigates to dashboard view
        public void NavigateToDashboard()
        {
            if (_mainFrame != null)
            {
                if (!IsDataLoaded)
                {
                    NavigateToWelcome();
                    return;
                }

                var dashboardView = new DashboardView();
                dashboardView.DataContext = _dashboardViewModel;
                _mainFrame.Navigate(dashboardView);
            }
        }

        // Initializes journal view model lazily
        private void InitializeJournalViewModel()
        {
            if (_journalViewModel == null)
            {
                _journalViewModel = new JournalViewModel();
                OnPropertyChanged(nameof(JournalViewModel));
            }
        }

        // Initializes drives view model lazily
        private void InitializeDrivesViewModel()
        {
            if (_drivesViewModel == null)
            {
                _drivesViewModel = new DrivesViewModel();
            }
        }

        // Initializes timeline view model lazily
        private void InitializeTimelineViewModel()
        {
            if (_timelineViewModel == null)
            {
                _timelineViewModel = new TimelineViewModel();
            }
        }

        // Initializes dashboard view model lazily
        private void InitializeDashboardViewModel()
        {
            if (_dashboardViewModel == null)
            {
                _dashboardViewModel = new DashboardViewModel();
            }
        }
        
        // Starts all trace collection processes
        public void StartAllTraces()
        {
            InitializeJournalViewModel();
            InitializeDrivesViewModel();
            InitializeTimelineViewModel();
            InitializeDashboardViewModel();
            
            _journalViewModel.StartLoading();
            
            _drivesViewModel.StartAnalysis();
            
            if (_timelineViewModel.SelectAndProcessCommand is AsyncRelayCommand asyncCommand)
            {
                _timelineViewModel.SelectAndProcessCommand.Execute(null);
            }
        }

        // Navigates to journal view
        private void NavigateToJournal()
        {
            if (_mainFrame == null)
                return;

            if (!IsDataLoaded)
            {
                NavigateToWelcome();
                return;
            }

            try
            {
                var journalView = new JournalView();
                bool result = _mainFrame.Navigate(journalView);

                if (result)
                {
                    journalView.DataContext = _journalViewModel;
                }
            }
            catch (Exception)
            {
            }
        }

        // Navigates to processes view
        private void NavigateToProcesses()
        {
            if (_mainFrame != null)
            {
                if (!IsDataLoaded)
                {
                    NavigateToWelcome();
                    return;
                }

                var processesView = new ProcessesView();
                _mainFrame.Navigate(processesView);
            }
        }

        // Navigates to timeline view
        private void NavigateToTimeline()
        {
            if (_mainFrame != null)
            {
                if (!IsDataLoaded)
                {
                    NavigateToWelcome();
                    return;
                }
                
                var timelineView = new TimelineView();
                timelineView.DataContext = _timelineViewModel;
                
                
                _mainFrame.Navigate(timelineView);
            }
        }

        // Navigates to drives view
        private void NavigateToDrives()
        {
            if (_mainFrame != null)
            {
                if (!IsDataLoaded)
                {
                    NavigateToWelcome();
                    return;
                }
                
                var drivesView = new DrivesView();
                drivesView.DataContext = _drivesViewModel;
                
                
                _mainFrame.Navigate(drivesView);
            }
        }

        // Empty placeholder navigation method
        private void NavigateToPlaceholder3()
        {
        }

        // Placeholder redirects to drives view
        private void NavigateToPlaceholder4()
        {
            NavigateToDrives();
        }

        // Refreshes all data and returns to welcome
        public void RefreshCurrentView()
        {
            IsDataLoaded = false;
            
            if (_journalViewModel != null)
            {
                _journalViewModel.UsnEntries.Clear();
                _journalViewModel = null;
            }
            
            if (_drivesViewModel != null)
            {
                _drivesViewModel.UsbEvents.Clear();
                _drivesViewModel.ConnectedVolumes.Clear();
                _drivesViewModel.DeviceOverview.Clear();
                _drivesViewModel = null;
            }
            
            if (_timelineViewModel != null)
            {
                _timelineViewModel.RegistryEntries.Clear();
                _timelineViewModel.FilteredRegistryEntries.Clear();
                _timelineViewModel = null;
            }
            
            if (_dashboardViewModel != null)
            {
                _dashboardViewModel = null;
            }
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            NavigateToWelcome();
        }

        // Property changed event for binding
        public event PropertyChangedEventHandler PropertyChanged;
        // Raises property changed notification
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Command implementation with execution control
    public class RelayCommand : ICommand
    {
        // Action to execute
        private readonly Action _execute;
        // Function determining execution possibility
        private readonly Func<bool> _canExecute;

        // Initializes command with action and condition
        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // Raised when execution ability changes
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        // Determines if command can execute
        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;
        // Executes command action
        public void Execute(object parameter) => _execute();
    }
}