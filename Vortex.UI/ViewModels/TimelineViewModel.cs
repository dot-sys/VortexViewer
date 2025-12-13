using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using Timeline.Core.Core;
using Timeline.Core.Models;
using System.Windows.Data;

namespace Vortex.UI.ViewModels
{
    public class FilterItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Description { get; set; }
        public string Source { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class TimestampPathKey : IEquatable<TimestampPathKey>
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Path { get; set; }

        public bool Equals(TimestampPathKey other)
        {
            if (other == null) return false;
            
            var thisTime = TruncateToSeconds(Timestamp);
            var otherTime = TruncateToSeconds(other.Timestamp);
            
            return thisTime == otherTime && 
                   string.Equals(Path, other.Path, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as TimestampPathKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + TruncateToSeconds(Timestamp).GetHashCode();
                hash = hash * 23 + (Path != null ? Path.GetHashCode() : 0);
                return hash;
            }
        }
        
        private static DateTimeOffset TruncateToSeconds(DateTimeOffset timestamp)
        {
            return new DateTimeOffset(
                timestamp.Year,
                timestamp.Month,
                timestamp.Day,
                timestamp.Hour,
                timestamp.Minute,
                timestamp.Second,
                0, // milliseconds = 0
                timestamp.Offset
            );
        }
    }

    public class TimelineViewModel : INotifyPropertyChanged
    {
        private const int DEBOUNCE_INTERVAL_MS = 300;
        private const int UI_BATCH_SIZE = 500; // Batch size for UI updates
        private const int PARALLEL_THRESHOLD = 5000; // Use parallel for large datasets

        // NEW: Windows path regex pattern (from ProcessesViewModel)
        private static readonly Regex WinPathRegex = new Regex(
            @"^(?:""?[a-zA-Z]\:|\\\\[^\\\/\:\*\?\<\>\|]+\\[^\\\/\:\*\?\<\>\|]*)\\(?:[^\\\/\:\*\?\<\>\|]+\\)*\w([^\\\/\:\*\?\<\>\|]*)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public ObservableCollection<RegistryEntry> RegistryEntries { get; } = new ObservableCollection<RegistryEntry>();
        public ObservableCollection<RegistryEntry> FilteredRegistryEntries { get; } = new ObservableCollection<RegistryEntry>();
        public ObservableCollection<FilterItem> AvailableDescriptions { get; } = new ObservableCollection<FilterItem>();
        public ObservableCollection<FilterItem> AvailableSources { get; } = new ObservableCollection<FilterItem>();

        private List<RegistryEntry> _originalEntries = new List<RegistryEntry>();

        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
        }

        private bool _isFiltering;
        public bool IsFiltering
        {
            get => _isFiltering;
            set { _isFiltering = value; OnPropertyChanged(nameof(IsFiltering)); }
        }

        private bool _uppercaseResults = true;
        public bool UppercaseResults
        {
            get => _uppercaseResults;
            set { _uppercaseResults = value; OnPropertyChanged(nameof(UppercaseResults)); }
        }

        private string _pathFilterText;
        public string PathFilterText
        {
            get => _pathFilterText;
            set
            {
                _pathFilterText = value;
                OnPropertyChanged(nameof(PathFilterText));
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        }

        private string _descriptionFilterText = string.Empty;
        public string DescriptionFilterText
        {
            get => _descriptionFilterText;
            set
            {
                _descriptionFilterText = value;
                OnPropertyChanged(nameof(DescriptionFilterText));
            }
        }

        private string _sourceFilterText = string.Empty;
        public string SourceFilterText
        {
            get => _sourceFilterText;
            set
            {
                _sourceFilterText = value;
                OnPropertyChanged(nameof(SourceFilterText));
            }
        }

        private string _timestampFilter;
        public string TimestampFilter
        {
            get => _timestampFilter;
            set
            {
                if (_timestampFilter != value)
                {
                    _timestampFilter = value;
                    OnPropertyChanged(nameof(TimestampFilter));
                    if (!IsLoading && !_isFiltering)
                    {
                        _debounceTimer.Stop();
                        _debounceTimer.Start();
                    }
                }
            }
        }

        private bool _showOnlyDeleted;
        public bool ShowOnlyDeleted
        {
            get => _showOnlyDeleted;
            set
            {
                _showOnlyDeleted = value;
                OnPropertyChanged(nameof(ShowOnlyDeleted));
                _ = ApplyFiltersAsync();
            }
        }

        private bool _showOnlyUnknown;
        public bool ShowOnlyUnknown
        {
            get => _showOnlyUnknown;
            set
            {
                _showOnlyUnknown = value;
                OnPropertyChanged(nameof(ShowOnlyUnknown));
                _ = ApplyFiltersAsync();
            }
        }

        private bool _showOnlyExe;
        public bool ShowOnlyExe
        {
            get => _showOnlyExe;
            set
            {
                _showOnlyExe = value;
                OnPropertyChanged(nameof(ShowOnlyExe));
                _ = ApplyFiltersAsync();
            }
        }

        private bool _showOnlyWinPaths;
        public bool ShowOnlyWinPaths
        {
            get => _showOnlyWinPaths;
            set
            {
                _showOnlyWinPaths = value;
                OnPropertyChanged(nameof(ShowOnlyWinPaths));
                _ = ApplyFiltersAsync();
            }
        }

        private bool _removeDuplicates = true;
        public bool RemoveDuplicates
        {
            get => _removeDuplicates;
            set
            {
                if (_removeDuplicates != value)
                {
                    _removeDuplicates = value;
                    OnPropertyChanged(nameof(RemoveDuplicates));
                    _ = ApplyFiltersAsync();
                }
            }
        }

        private bool _hideSystem32Logs;
        public bool HideSystem32Logs
        {
            get => _hideSystem32Logs;
            set
            {
                if (_hideSystem32Logs != value)
                {
                    _hideSystem32Logs = value;
                    OnPropertyChanged(nameof(HideSystem32Logs));
                    _ = ApplyFiltersAsync();
                }
            }
        }

        private bool _coloredResults;
        public bool ColoredResults
        {
            get => _coloredResults;
            set
            {
                if (_coloredResults != value)
                {
                    _coloredResults = value;
                    OnPropertyChanged(nameof(ColoredResults));
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        var view = CollectionViewSource.GetDefaultView(FilteredRegistryEntries);
                        if (view != null)
                        {
                            view.Refresh();
                        }
                    }, DispatcherPriority.Background);
                }
            }
        }

        private int _currentPage = 1;
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (_currentPage != value)
                {
                    _currentPage = value;
                    OnPropertyChanged(nameof(CurrentPage));
                    UpdatePage();
                }
            }
        }

        public int PageSize { get; set; } = 20;
        
        public int TotalPages => (_cachedFilteredEntries != null && _cachedFilteredEntries.Count > 0)
            ? (int)Math.Ceiling((double)_cachedFilteredEntries.Count / PageSize)
            : 1;
            
        public int FilteredEntriesCount => _cachedFilteredEntries?.Count ?? 0;
        
        public int TotalEntries => RegistryEntries?.Count ?? 0;

        private List<RegistryEntry> _cachedFilteredEntries = new List<RegistryEntry>();

        public ICommand SelectAndProcessCommand { get; }
        public ICommand ShowParseInfoCommand { get; }
        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand Next5PagesCommand { get; }
        public ICommand Prev5PagesCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }

        private CancellationTokenSource _filterCancellationTokenSource;
        private readonly DispatcherTimer _debounceTimer;

        public TimelineViewModel()
        {
            SelectAndProcessCommand = new AsyncRelayCommand(ProcessHivesAsync);
            ShowParseInfoCommand = new SimpleRelayCommand(_ => ShowParseInfo());
            
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DEBOUNCE_INTERVAL_MS) };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                _ = ApplyFiltersAsync();
            };

            NextPageCommand = new SimpleRelayCommand(_ =>
            {
                if (CurrentPage < TotalPages)
                    CurrentPage++;
            });
            
            PrevPageCommand = new SimpleRelayCommand(_ =>
            {
                if (CurrentPage > 1)
                    CurrentPage--;
            });

            Next5PagesCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = Math.Min(CurrentPage + 5, TotalPages);
            });
            
            Prev5PagesCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = Math.Max(CurrentPage - 5, 1);
            });

            FirstPageCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = 1;
            });
            
            LastPageCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = TotalPages;
            });
        }

        private async Task ProcessHivesAsync()
        {
            IsLoading = true;
            Status = "Finding and processing hives...";

            try
            {
                var result = await Task.Run(async () =>
                {
                    return await RegistryExtractor.ExtractFromStandardLocationsAsync(
                        new Progress<string>(update => 
                        {
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                Status = update;
                            }));
                        }),
                        default,
                        UppercaseResults).ConfigureAwait(false);
                }).ConfigureAwait(false);

                var sortedEntries = result.Entries.OrderByDescending(e => e.Timestamp).ToList();

                _originalEntries = sortedEntries;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RegistryEntries.Clear();
                    FilteredRegistryEntries.Clear();
                    
                    foreach (var entry in sortedEntries)
                    {
                        RegistryEntries.Add(entry);
                    }
                    
                    UpdateFilterOptions();
                    
                    IsLoading = false;
                    
                    _ = ApplyFiltersAsync();
                    
                    OnPropertyChanged(nameof(TotalEntries));
                });

                Status = $"{RegistryEntries.Count} Entries";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                IsLoading = false;
            }
        }

        private void UpdateFilterOptions()
        {
            var descriptions = RegistryEntries.Select(x => x.Description).Distinct().OrderBy(x => x);
            var sources = RegistryEntries.Select(x => x.Source).Distinct().OrderBy(x => x);

            AvailableDescriptions.Clear();
            foreach (var d in descriptions)
            {
                var item = new FilterItem { Description = d };
                item.PropertyChanged += (s, e) => 
                {
                    if (e.PropertyName == nameof(FilterItem.IsSelected))
                        _ = ApplyFiltersAsync();
                };
                AvailableDescriptions.Add(item);
            }

            AvailableSources.Clear();
            foreach (var s in sources)
            {
                var item = new FilterItem { Source = s };
                item.PropertyChanged += (s2, e) => 
                {
                    if (e.PropertyName == nameof(FilterItem.IsSelected))
                        _ = ApplyFiltersAsync();
                };
                AvailableSources.Add(item);
            }
        }

        private void UpdatePage()
        {
            if (IsLoading || IsFiltering)
                return;

            var pageEntries = _cachedFilteredEntries
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            FilteredRegistryEntries.Clear();
            foreach (var entry in pageEntries)
            {
                FilteredRegistryEntries.Add(entry);
            }
        }

        public void ApplyFilters()
        {
            _ = ApplyFiltersAsync();
        }

        public async Task ApplyFiltersAsync()
        {
            if (IsLoading || IsFiltering) return;

            _filterCancellationTokenSource?.Cancel();
            _filterCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _filterCancellationTokenSource.Token;

            try
            {
                IsFiltering = true;

                var selectedDescriptions = new HashSet<string>(AvailableDescriptions.Where(x => x.IsSelected).Select(x => x.Description));
                var selectedSources = new HashSet<string>(AvailableSources.Where(x => x.IsSelected).Select(x => x.Source));
                var pathFilter = PathFilterText;
                var timestampFilter = TimestampFilter;
                var showOnlyDeleted = ShowOnlyDeleted;
                var showOnlyUnknown = ShowOnlyUnknown;
                var showOnlyExe = ShowOnlyExe;
                var showOnlyWinPaths = ShowOnlyWinPaths;
                var removeDuplicates = RemoveDuplicates;
                var hideSystem32Logs = HideSystem32Logs;

                var filtered = await Task.Run(() =>
                {
                    var entries = _originalEntries.ToList();
                    if (cancellationToken.IsCancellationRequested) return null;

                    if (removeDuplicates && entries.Count > 0)
                    {
                        entries = RemoveDuplicatesWithTimeWindow(entries);
                    }

                    // STEP 2: Apply other filters
                    // Use parallel processing for large datasets
                    var query = entries.Count > PARALLEL_THRESHOLD 
                        ? entries.AsParallel().AsOrdered() 
                        : entries.AsEnumerable();

                    if (selectedDescriptions.Any())
                        query = query.Where(x => selectedDescriptions.Contains(x.Description));

                    if (selectedSources.Any())
                        query = query.Where(x => selectedSources.Contains(x.Source));

                    if (!string.IsNullOrWhiteSpace(pathFilter))
                        query = query.Where(x => !string.IsNullOrEmpty(x.Path) && 
                            x.Path.IndexOf(pathFilter, StringComparison.OrdinalIgnoreCase) >= 0);

                    if (!string.IsNullOrWhiteSpace(timestampFilter))
                    {
                        query = query.Where(x => 
                        {
                            var timestampStr = x.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
                            return Vortex.UI.Controls.TimestampMask.MatchesWildcardPattern(timestampStr, timestampFilter);
                        });
                    }

                    if (showOnlyDeleted)
                        query = query.Where(x => !string.IsNullOrEmpty(x.Modified) && 
                            x.Modified.Equals("Deleted", StringComparison.OrdinalIgnoreCase));

                    if (showOnlyUnknown)
                        query = query.Where(x => !string.IsNullOrEmpty(x.Modified) && 
                            x.Modified.Equals("Unknown", StringComparison.OrdinalIgnoreCase));

                    if (showOnlyExe)
                        query = query.Where(x => !string.IsNullOrEmpty(x.Path) && 
                            x.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

                    if (showOnlyWinPaths)
                        query = query.Where(x => !string.IsNullOrEmpty(x.Path) && 
                            WinPathRegex.IsMatch(x.Path));

                    if (hideSystem32Logs)
                        query = query.Where(x => string.IsNullOrEmpty(x.Path) || 
                            (x.Path.IndexOf(@"\Windows\System32\", StringComparison.OrdinalIgnoreCase) == -1 &&
                             !x.Path.StartsWith(@"C:\Windows\System32\", StringComparison.OrdinalIgnoreCase)));

                    if (cancellationToken.IsCancellationRequested) return null;

                    return query.ToList();
                }, cancellationToken);

                if (cancellationToken.IsCancellationRequested || filtered == null)
                    return;

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _cachedFilteredEntries = filtered;
                    CurrentPage = 1;
                    OnPropertyChanged(nameof(FilteredEntriesCount));
                    OnPropertyChanged(nameof(TotalPages));
                    
                    IsFiltering = false;
                    
                    UpdatePage();
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
                IsFiltering = false;
            }
            catch (Exception ex)
            {
                Status = $"Filter error: {ex.Message}";
                IsFiltering = false;
            }
        }

        private List<RegistryEntry> RemoveDuplicatesWithTimeWindow(List<RegistryEntry> entries)
        {
            var deduplicated = new List<RegistryEntry>();
            var seen = new Dictionary<string, DateTimeOffset>();
            
            // Process entries in chronological order (newest to oldest, since they're already sorted that way)
            foreach (var entry in entries)
            {
                var path = entry.Path ?? string.Empty;
                var timestamp = TruncateToSeconds(entry.Timestamp);
                
                if (seen.TryGetValue(path, out var lastKeptTimestamp))
                {
                    var timeDifference = (lastKeptTimestamp - timestamp).TotalSeconds;
                    
                    if (timeDifference >= 0 && timeDifference <= 5)
                    {
                        continue;
                    }
                }
                
                deduplicated.Add(entry);
                seen[path] = timestamp;
            }
            
            return deduplicated;
        }
        
        private static DateTimeOffset TruncateToSeconds(DateTimeOffset timestamp)
        {
            return new DateTimeOffset(
                timestamp.Year,
                timestamp.Month,
                timestamp.Day,
                timestamp.Hour,
                timestamp.Minute,
                timestamp.Second,
                0, // milliseconds = 0
                timestamp.Offset
            );
        }

        private void RefreshFilteredView()
        {
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                var view = CollectionViewSource.GetDefaultView(FilteredRegistryEntries);
                view?.Refresh();
            }, DispatcherPriority.Background);
        }

        private void ShowParseInfo()
        {
            var allSources = new List<string>
            {
                "CIT Module",
                "CIT System",
                "CompatAssist Persisted",
                "CompatAssist Store",
                "JumpListData",
                "MuiCache",
                "RADAR HeapLeakDetection",
                "RecentDocs",
                "Registry",
                "Run",
                "RunMRU",
                "RunOnce",
                "TypedPaths",
                "UserAssist",
                "WinRAR History",
                
                "Amcache",
                "BAM",
                "EventLog",
                "Prefetch",
                "ShimCache",
                "WER"
            };

            var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            
            foreach (var source in allSources)
            {
                sourceCounts[source] = 0;
            }
            
            foreach (var entry in RegistryEntries)
            {
                if (!string.IsNullOrEmpty(entry.Source))
                {
                    if (sourceCounts.ContainsKey(entry.Source))
                    {
                        sourceCounts[entry.Source]++;
                    }
                    else
                    {
                        if (!sourceCounts.TryGetValue(entry.Source, out int existingCount))
                        {
                            existingCount = 0;
                        }
                        sourceCounts[entry.Source] = existingCount + 1;
                    }
                }
            }
            
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("Timeline Parse Info - Entry Counts by Source");
            lines.AppendLine();
            
            foreach (var source in sourceCounts.OrderBy(kvp => kvp.Key))
            {
                var count = source.Value;
                var paddedSource = source.Key.PadRight(25);
                var countStr = count.ToString("N0");
                
                // Note: We can't actually color text in the popup (it's plain text)
                // but we'll add a marker for 0-count sources
                if (count == 0)
                {
                    lines.AppendLine($"[!] {paddedSource} {countStr}");
                }
                else
                {
                    lines.AppendLine($"    {paddedSource} {countStr}");
                }
            }
            
            lines.AppendLine();
            lines.AppendLine("[!] = Zero entries - possible data manipulation or missing artifact");
            lines.AppendLine();
            lines.AppendLine($"Total Entries: {RegistryEntries.Count:N0}");
            
            var message = lines.ToString();
            System.Media.SystemSounds.Beep.Play();
            
            Vortex.UI.Views.PopupHelper.ShowTextPopup("Timeline Parse Info", message);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute)
        {
            _execute = execute;
        }

        public bool CanExecute(object parameter) => !_isExecuting;

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;
            _isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await _execute();
            }
            finally
            {
                _isExecuting = false;
                RaiseCanExecuteChanged();
            }
        }

        private void RaiseCanExecuteChanged() =>
            CommandManager.InvalidateRequerySuggested();
    }
}
