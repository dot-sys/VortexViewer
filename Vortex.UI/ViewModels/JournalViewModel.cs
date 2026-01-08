using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using VortexViewer.Core;
using VortexViewer.Journal.Core.Models;
using VortexViewer.Journal.Core.Util;
using Vortex.UI.ViewModels;

// ViewModel layer for UI binding
namespace Vortex.UI.ViewModels
{
    // ViewModel for USN journal forensic analysis
    public class JournalViewModel : INotifyPropertyChanged
    {
        // Debounce interval for filter updates
        private const int DEBOUNCE_INTERVAL_MS = 25;
        // Idle timer check interval seconds
        private const int IDLE_TIMER_INTERVAL_SECONDS = 2;
        // Idle threshold before cleanup
        private const int IDLE_THRESHOLD_COUNT = 3;
        // Typing detection threshold milliseconds
        private const int TYPING_INCOMPLETE_THRESHOLD_MS = 500;
        // Parallel processing entry threshold
        private const int PARALLEL_PROCESSING_THRESHOLD = 10000;
        
        // Collection of journal entries displayed
        public ObservableCollection<JournalEntry> UsnEntries { get; private set; }

        // Status message for extraction progress
        public string ExtractionStatus
        {
            get => _extractionStatus;
            set { _extractionStatus = value; OnPropertyChanged(); }
        }
        // Backing field for extraction status
        private string _extractionStatus = "Ready.";

        // Command to show journal info popup
        public ICommand ShowInfoCommand { get; }

        // Stopwatch for tracking load time
        private Stopwatch _totalLoadStopwatch;

        // Backing field for path filter
        private string _pathFilter;
        // Filter text for file paths
        public string PathFilter
        {
            get => _pathFilter;
            set
            {
                if (_pathFilter != value)
                {
                    _pathFilter = value;
                    OnPropertyChanged();
                    if (!IsLoading && !_isRebuildingBaseSet)
                    {
                        _debounceTimer.Stop();
                        _debounceTimer.Start();
                    }
                }
            }
        }

        private string _extensionFilter;
        public string ExtensionFilter
        {
            get => _extensionFilter;
            set
            {
                if (_extensionFilter != value)
                {
                    _extensionFilter = value;
                    _lastExtensionFilterChange = DateTime.Now;
                    OnPropertyChanged();
                    if (!IsLoading && !_isRebuildingBaseSet)
                    {
                        _debounceTimer.Stop();
                        _debounceTimer.Start();
                    }
                }
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
                    OnPropertyChanged();
                    if (!IsLoading && !_isRebuildingBaseSet)
                    {
                        _debounceTimer.Stop();
                        _debounceTimer.Start();
                    }
                }
            }
        }

        private JournalEntry[] _allEntries;
        private JournalEntry[] _baseEntries;

        private Dictionary<string, List<JournalEntry>> _reasonIndices = new Dictionary<string, List<JournalEntry>>();

        private bool _isRebuildingBaseSet = false;

        private List<JournalEntry> _cachedFilteredEntries = new List<JournalEntry>();

        private readonly DispatcherTimer _debounceTimer;
        private Regex _compiledExtRegex;

        private string _simplePathFilter;
        private string _simpleExtFilter;
        private string _simpleTimestampFilter;

        // NEW: Graceful degradation for typing detection
        private string _lastSuccessfulExtFilter;
        private List<JournalEntry> _lastSuccessfulResults;
        private bool _isTypingIncomplete = false;
        private DateTime _lastExtensionFilterChange = DateTime.Now;

        private readonly Dictionary<string, Regex> _regexCache = new Dictionary<string, Regex>();
        private readonly int _maxRegexCache = 10;

        private readonly Dictionary<string, List<JournalEntry>> _intermediateCache = new Dictionary<string, List<JournalEntry>>();
        private readonly int _maxCacheSize = 20;
        private bool _isBackgroundFilteringActive = false;

        private readonly DispatcherTimer _idleTimer;
        private int _uiIdleCount = 0;

        private CancellationTokenSource _filterCancellationTokenSource;

        private bool _filterCreated;
        public bool FilterCreated
        {
            get => _filterCreated;
            set
            {
                if (_filterCreated != value)
                {
                    _filterCreated = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterDeleted;
        public bool FilterDeleted
        {
            get => _filterDeleted;
            set
            {
                if (_filterDeleted != value)
                {
                    _filterDeleted = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterRenames;
        public bool FilterRenames
        {
            get => _filterRenames;
            set
            {
                if (_filterRenames != value)
                {
                    _filterRenames = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterOverwrite;
        public bool FilterOverwrite
        {
            get => _filterOverwrite;
            set
            {
                if (_filterOverwrite != value)
                {
                    _filterOverwrite = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterExtended;
        public bool FilterExtended
        {
            get => _filterExtended;
            set
            {
                if (_filterExtended != value)
                {
                    _filterExtended = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterTruncation;
        public bool FilterTruncation
        {
            get => _filterTruncation;
            set
            {
                if (_filterTruncation != value)
                {
                    _filterTruncation = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterDataChanged;
        public bool FilterDataChanged
        {
            get => _filterDataChanged;
            set
            {
                if (_filterDataChanged != value)
                {
                    _filterDataChanged = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterBasic;
        public bool FilterBasic
        {
            get => _filterBasic;
            set
            {
                if (_filterBasic != value)
                {
                    _filterBasic = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterClose;
        public bool FilterClose
        {
            get => _filterClose;
            set
            {
                if (_filterClose != value)
                {
                    _filterClose = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _filterError;
        public bool FilterError
        {
            get => _filterError;
            set
            {
                if (_filterError != value)
                {
                    _filterError = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RefilterAsync();
                }
            }
        }

        private bool _reduceDuplicates = true;
        public bool ReduceDuplicates
        {
            get => _reduceDuplicates;
            set
            {
                if (_reduceDuplicates != value)
                {
                    _reduceDuplicates = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RebuildBaseSetAsync();
                }
            }
        }

        private bool _filterSpamlogs;
        public bool FilterSpamlogs
        {
            get => _filterSpamlogs;
            set
            {
                if (_filterSpamlogs != value)
                {
                    _filterSpamlogs = value;
                    OnPropertyChanged();
                    CurrentPage = 1;
                    if (!IsLoading && !_isRebuildingBaseSet)
                        RebuildBaseSetAsync();
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
                    OnPropertyChanged();
                    UpdatePage();
                }
            }
        }
        public int PageSize { get; set; } = 20;
        public int TotalPages => (_cachedFilteredEntries != null && _cachedFilteredEntries.Count > 0)
            ? (int)Math.Ceiling((double)_cachedFilteredEntries.Count / PageSize)
            : 1;
        public int FilteredEntriesCount => _cachedFilteredEntries?.Count ?? 0;

        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand Next5PagesCommand { get; }
        public ICommand Prev5PagesCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNotLoading));
                }
            }
        }

        public bool IsNotLoading => !IsLoading && !_isRebuildingBaseSet;

        public JournalViewModel()
        {
            UsnEntries = new ObservableCollection<JournalEntry>();
            ShowInfoCommand = new SimpleRelayCommand(_ => ShowInfo());
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DEBOUNCE_INTERVAL_MS) };
            _debounceTimer.Tick += DebounceTimer_Tick;
            
            _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(IDLE_TIMER_INTERVAL_SECONDS) };
            _idleTimer.Tick += IdleTimer_Tick;
            _idleTimer.Start();
            
            NextPageCommand = new SimpleRelayCommand(_ =>
            {
                if (CurrentPage < TotalPages)
                    CurrentPage++;
                ResetIdleTimer();
            });
            PrevPageCommand = new SimpleRelayCommand(_ =>
            {
                if (CurrentPage > 1)
                    CurrentPage--;
                ResetIdleTimer();
            });

            Next5PagesCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = Math.Min(CurrentPage + 5, TotalPages);
                ResetIdleTimer();
            });
            Prev5PagesCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = Math.Max(CurrentPage - 5, 1);
                ResetIdleTimer();
            });

            FirstPageCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = 1;
                ResetIdleTimer();
            });
            LastPageCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = TotalPages;
                ResetIdleTimer();
            });

            _filterSpamlogs = true;
            _reduceDuplicates = true;
            
            // Notify UI that these properties have their initial values
            OnPropertyChanged(nameof(FilterSpamlogs));
            OnPropertyChanged(nameof(ReduceDuplicates));
        }

        private void ResetIdleTimer()
        {
            _uiIdleCount = 0;
            _idleTimer.Stop();
            _idleTimer.Start();
        }

        private void IdleTimer_Tick(object sender, EventArgs e)
        {
            _uiIdleCount++;
            
            if (_uiIdleCount >= IDLE_THRESHOLD_COUNT)
            {
                _idleTimer.Stop();
                Task.Run(() =>
                {
                    try
                    {
                        CleanupCaches();
                        
                        if (GC.GetTotalMemory(false) > 500_000_000)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized); // Gen 0 only, not forced
                        }
                    }
                    catch
                    {
                    }
                });
            }
        }

        private void CleanupCaches()
        {
            if (_intermediateCache.Count > _maxCacheSize / 2)
                _intermediateCache.Clear();
                
            if (_regexCache.Count > _maxRegexCache / 2)
                _regexCache.Clear();
            
            JournalEntry.ClearCaches();
        }

        private async void RebuildBaseSetAsync()
        {
            if (_allEntries == null || _allEntries.Length == 0) return;

            _isRebuildingBaseSet = true;
            OnPropertyChanged(nameof(IsNotLoading));
            ExtractionStatus = "Rebuilding filter indices...";

            try
            {
                var result = await Task.Run(() =>
                {
                    var baseEntries = ApplyBaseFiltering(_allEntries);
                    
                    var reasonIndices = BuildReasonIndices(baseEntries);
                    
                    return new { BaseEntries = baseEntries, ReasonIndices = reasonIndices };
                });

                _baseEntries = result.BaseEntries;
                _reasonIndices = result.ReasonIndices;
                
                _intermediateCache.Clear();
                
                _cachedFilteredEntries = _baseEntries.ToList();
                
                CurrentPage = 1;
                UpdatePage();
                OnPropertyChanged(nameof(FilteredEntriesCount));
                OnPropertyChanged(nameof(TotalPages));
            }
            finally
            {
                _isRebuildingBaseSet = false;
                OnPropertyChanged(nameof(IsNotLoading));
            }
        }

        private JournalEntry[] ApplyBaseFiltering(JournalEntry[] entries)
        {
            IEnumerable<JournalEntry> result = entries;
            
            if (_filterSpamlogs)
            {
                result = result.Where(e => !IsSpamlog(e));
            }
            
            if (_reduceDuplicates && entries.Length > 0)
            {
                var reduced = new List<JournalEntry>();
                JournalEntry? previous = null;
                foreach (var entry in result)
                {
                    if (previous.HasValue &&
                        entry.Timestamp == previous.Value.Timestamp &&
                        entry.FullPath == previous.Value.FullPath &&
                        entry.ReasonString == previous.Value.ReasonString)
                    {
                        continue;
                    }
                    reduced.Add(entry);
                    previous = entry;
                }
                return reduced.ToArray();
            }
            
            return result.ToArray();
        }

        private Dictionary<string, List<JournalEntry>> BuildReasonIndices(JournalEntry[] baseEntries)
        {
            var indices = new Dictionary<string, List<JournalEntry>>();
            
            // Initialize all possible reason lists
            var reasonTypes = new[] { "Created", "Deleted", "RenamedFrom", "RenamedTo", 
                                     "DataChange", "Overwrite", "Extended", "Truncation", "Basic", "Close", "ERROR" };
            
            foreach (var reasonType in reasonTypes)
            {
                indices[reasonType] = new List<JournalEntry>();
            }
            
            foreach (var entry in baseEntries)
            {
                if (indices.ContainsKey(entry.ReasonString))
                {
                    indices[entry.ReasonString].Add(entry);
                }
            }
            
            return indices;
        }

        private Regex GetOrCreateRegex(string pattern, RegexOptions options)
        {
            var key = $"{pattern}|{options}";
            if (_regexCache.TryGetValue(key, out var cached))
                return cached;
                
            try 
            {
                var regex = new Regex(pattern, options);
                if (_regexCache.Count >= _maxRegexCache)
                {
                    var oldestKey = _regexCache.Keys.First();
                    _regexCache.Remove(oldestKey);
                }
                _regexCache[key] = regex;
                return regex;
            }
            catch 
            { 
                return null; 
            }
        }

        private void DebounceTimer_Tick(object sender, EventArgs e)
        {
            _debounceTimer.Stop();
            ResetIdleTimer();

            var oldSimplePathFilter = _simplePathFilter;
            var oldSimpleExtFilter = _simpleExtFilter;
            var oldCompiledExtRegex = _compiledExtRegex;
            var oldSimpleTimestampFilter = _simpleTimestampFilter;

            // Path filter logic - ONLY simple string search
            if (!string.IsNullOrEmpty(PathFilter))
            {
                _simplePathFilter = PathFilter.ToLowerInvariant();
            }
            else
            {
                _simplePathFilter = null;
            }

            if (!string.IsNullOrEmpty(ExtensionFilter))
            {
                string extFilter = ExtensionFilter.StartsWith(".") ? ExtensionFilter.Substring(1) : ExtensionFilter;
                
                extFilter = extFilter.Trim();
                
                if (string.IsNullOrEmpty(extFilter))
                {
                    _compiledExtRegex = null;
                    _simpleExtFilter = null;
                    _isTypingIncomplete = false;
                }
                else
                {
                    var timeSinceLastChange = DateTime.Now - _lastExtensionFilterChange;
                    bool isLikelyIncompleteTyping = false;
                    
                    
                    if (extFilter.Length == 1)
                    {
                        isLikelyIncompleteTyping = true;
                    }
                    else if (extFilter.Length == 2)
                    {
                        isLikelyIncompleteTyping = timeSinceLastChange.TotalMilliseconds < TYPING_INCOMPLETE_THRESHOLD_MS;
                    }
                    
                    _isTypingIncomplete = isLikelyIncompleteTyping;
                    
                    bool needsRegex = extFilter.Contains("|");
                    
                    if (extFilter.EndsWith("|"))
                        extFilter = extFilter.Substring(0, extFilter.Length - 1);

                    if (needsRegex)
                    {
                        _simpleExtFilter = null;
                        string pattern = "^(" + extFilter + ")$";
                        _compiledExtRegex = GetOrCreateRegex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    }
                    else
                    {
                        _compiledExtRegex = null;
                        _simpleExtFilter = extFilter.ToLowerInvariant();
                    }
                }
            }
            else
            {
                _compiledExtRegex = null;
                _simpleExtFilter = null;
                _isTypingIncomplete = false;
            }

            if (!string.IsNullOrEmpty(TimestampFilter))
            {
                _simpleTimestampFilter = TimestampFilter.ToLowerInvariant();
            }
            else
            {
                _simpleTimestampFilter = null;
            }

            // Only clear cache if the expensive filters actually changed
            bool expensiveFiltersChanged = 
                oldSimplePathFilter != _simplePathFilter ||
                oldSimpleExtFilter != _simpleExtFilter ||
                oldSimpleTimestampFilter != _simpleTimestampFilter ||
                !ReferenceEquals(oldCompiledExtRegex, _compiledExtRegex);

            if (expensiveFiltersChanged)
            {
                _intermediateCache.Clear();
            }

            RefilterAsync();
        }

        private async void RefilterAsync()
        {
            if (IsLoading || _isRebuildingBaseSet) return;

            // Cancel any ongoing filtering operation
            _filterCancellationTokenSource?.Cancel();
            _filterCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _filterCancellationTokenSource.Token;

            try
            {
                var quickResult = await GetFirstPageQuick();
                if (quickResult.HasValue && !cancellationToken.IsCancellationRequested)
                {
                    var results = quickResult.Value.allResults;
                    
                    if (_isTypingIncomplete && results.Count == 0 && _lastSuccessfulResults != null && _lastSuccessfulResults.Count > 0)
                    {
                        _cachedFilteredEntries = _lastSuccessfulResults;
                        ExtractionStatus = $"Showing {_lastSuccessfulResults.Count} entries from '{_lastSuccessfulExtFilter}' (typing in progress...)";
                    }
                    else
                    {
                        _cachedFilteredEntries = results;
                        
                        if (results.Count > 0 && !_isTypingIncomplete && !string.IsNullOrEmpty(_simpleExtFilter))
                        {
                            _lastSuccessfulResults = new List<JournalEntry>(results);
                            _lastSuccessfulExtFilter = _simpleExtFilter;
                        }
                        
                        if (results.Count == 0 && !_isTypingIncomplete && !string.IsNullOrEmpty(_simpleExtFilter))
                        {
                            ExtractionStatus = $"No results for '{_simpleExtFilter}' - 0 entries found";
                        }
                        else
                        {
                            ExtractionStatus = $"Loaded {quickResult.Value.firstPage.Count}/{results.Count} entries (Page 1 of {TotalPages})";
                        }
                    }
                    
                    
                    CurrentPage = 1;
                    
                    var displayEntries = _cachedFilteredEntries.Take(PageSize).ToList();
                    UsnEntries.Clear();
                    foreach (var entry in displayEntries)
                        UsnEntries.Add(entry);
                    OnPropertyChanged(nameof(FilteredEntriesCount));
                    OnPropertyChanged(nameof(TotalPages));
                    
                    return;
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    _isBackgroundFilteringActive = true;
                    ExtractionStatus = "Filtering entries...";
                    
                    List<JournalEntry> fullResult = await Task.Run(() => DoFullFiltering(), cancellationToken);
                    
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _isBackgroundFilteringActive = false;
                        
                        if (_isTypingIncomplete && fullResult.Count == 0 && _lastSuccessfulResults != null && _lastSuccessfulResults.Count > 0)
                        {
                            _cachedFilteredEntries = _lastSuccessfulResults;
                            ExtractionStatus = $"Showing {_lastSuccessfulResults.Count} entries from '{_lastSuccessfulExtFilter}' (typing in progress...)";
                        }
                        else
                        {
                            _cachedFilteredEntries = fullResult;
                            
                            if (fullResult.Count > 0 && !_isTypingIncomplete && !string.IsNullOrEmpty(_simpleExtFilter))
                            {
                                _lastSuccessfulResults = new List<JournalEntry>(fullResult);
                                _lastSuccessfulExtFilter = _simpleExtFilter;
                            }
                            
                            if (fullResult.Count == 0 && !_isTypingIncomplete && !string.IsNullOrEmpty(_simpleExtFilter))
                            {
                                ExtractionStatus = $"No results for '{_simpleExtFilter}' - 0 entries found";
                            }
                        }
                        
                        CurrentPage = 1;
                        UpdatePage();
                        OnPropertyChanged(nameof(FilteredEntriesCount));
                        OnPropertyChanged(nameof(TotalPages));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _isBackgroundFilteringActive = false;
            }
            catch
            {
                _isBackgroundFilteringActive = false;
            }
        }

        private async Task<(List<JournalEntry> firstPage, List<JournalEntry> allResults)?> GetFirstPageQuick()
        {
            var expensiveKey = $"{_simplePathFilter ?? ""}|{_simpleExtFilter ?? ""}|{_simpleTimestampFilter ?? ""}|{(_compiledExtRegex?.ToString() ?? "")}";
            
            if (_intermediateCache.TryGetValue(expensiveKey, out var cachedExpensiveResult))
            {
                var quickFiltered = await Task.Run(() => ApplyToggleFilters(cachedExpensiveResult));
                var firstPage = quickFiltered.Take(PageSize).ToList();
                
                return (firstPage, quickFiltered);
            }
            
            return null;
        }

        private List<JournalEntry> DoFullFiltering()
        {
            IEnumerable<JournalEntry> list = _baseEntries ?? _allEntries ?? new JournalEntry[0];

            list = ApplyExpensiveFiltersToBaseSet(list);
            
            list = ApplyToggleFilters(list.ToList());
            
            return list.ToList();
        }

        private List<JournalEntry> ApplyExpensiveFiltersToBaseSet(IEnumerable<JournalEntry> entries)
        {
            var expensiveKey = $"{_simplePathFilter ?? ""}|{_simpleExtFilter ?? ""}|{_simpleTimestampFilter ?? ""}|{(_compiledExtRegex?.ToString() ?? "")}";
            
            if (_intermediateCache.TryGetValue(expensiveKey, out var cached))
            {
                return cached;
            }

            var entryList = entries.ToList();
            var useParallel = entryList.Count > PARALLEL_PROCESSING_THRESHOLD;
            var result = useParallel ? entryList.AsParallel().AsOrdered() : entryList.AsEnumerable();

            if (_simpleExtFilter != null)
            {
                result = result.Where(e => e.GetFileExtension() == _simpleExtFilter);
            }
            else if (_compiledExtRegex != null)
            {
                result = result.Where(e => _compiledExtRegex.IsMatch(e.GetFileExtension()));
            }

            if (!string.IsNullOrEmpty(_simplePathFilter))
            {
                var pathFilter = _simplePathFilter;
                result = result.Where(e => e.GetLowerPath().Contains(pathFilter));
            }

            if (!string.IsNullOrEmpty(_simpleTimestampFilter))
            {
                var timestampFilter = _simpleTimestampFilter;
                result = result.Where(e => Vortex.UI.Controls.TimestampMask.MatchesWildcardPattern(e.Timestamp, timestampFilter));
            }

            var finalResult = result.ToList();

            if (_intermediateCache.Count >= _maxCacheSize)
            {
                var oldestKey = _intermediateCache.Keys.First();
                _intermediateCache.Remove(oldestKey);
            }
            _intermediateCache[expensiveKey] = finalResult;

            return finalResult;
        }

        private List<JournalEntry> ApplyToggleFilters(List<JournalEntry> entries)
        {
            if (_baseEntries != null && _reasonIndices != null && entries.Count == _baseEntries.Length)
            {
                // Check if this is our base array by comparing first few elements
                bool isBaseArray = entries.Count > 0 && _baseEntries.Length > 0 && 
                                  entries[0].FileTime == _baseEntries[0].FileTime;
                
                if (isBaseArray)
                {
                    return ApplyToggleFiltersUsingIndices();
                }
            }
            
            return ApplyToggleFiltersLegacy(entries);
        }

        private List<JournalEntry> ApplyToggleFiltersUsingIndices()
        {
            var reasonFilters = new List<string>();
            if (FilterCreated) reasonFilters.Add("Created");
            if (FilterDeleted) reasonFilters.Add("Deleted");
            if (FilterRenames)
            {
                reasonFilters.Add("RenameFrom");
                reasonFilters.Add("RenameTo");
            }
            if (FilterDataChanged)
                reasonFilters.AddRange(new[] { "DataChange", "Overwrite", "Extended", "Truncation", "Basic" });
            if (FilterClose) reasonFilters.Add("Close");
            if (FilterError) reasonFilters.Add("ERROR");

            if (!reasonFilters.Any())
                return _baseEntries.ToList();

            var resultList = new List<JournalEntry>();
            
            foreach (var entry in _baseEntries)
            {
                if (reasonFilters.Contains(entry.ReasonString))
                {
                    resultList.Add(entry);
                }
            }
            
            return resultList;
        }

        private List<JournalEntry> ApplyToggleFiltersLegacy(List<JournalEntry> entries)
        {
            var reasonFilters = new List<string>();
            if (FilterCreated) reasonFilters.Add("Created");
            if (FilterDeleted) reasonFilters.Add("Deleted");
            if (FilterRenames)
            {
                reasonFilters.Add("RenameFrom");
                reasonFilters.Add("RenameTo");
            }
            if (FilterDataChanged)
                reasonFilters.AddRange(new[] { "DataChange", "Overwrite", "Extended", "Truncation", "Basic" });
            if (FilterClose) reasonFilters.Add("Close");
            if (FilterError) reasonFilters.Add("ERROR");

            if (!reasonFilters.Any())
                return entries;

            var result = entries.Where(e => reasonFilters.Contains(e.ReasonString)).ToList();

            return result;
        }

        private async void UpdatePage()
        {
            if (IsLoading || _isBackgroundFilteringActive || _isRebuildingBaseSet)
                return;

            var pageEntries = _cachedFilteredEntries
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                UsnEntries.Clear();
                foreach (var entry in pageEntries)
                {
                    UsnEntries.Add(entry);
                }
            }), DispatcherPriority.Background);
            
            ExtractionStatus = $"Loaded {FilteredEntriesCount}/{TotalEntries} entries (Page {CurrentPage} of {TotalPages})";
        }

        private async Task LoadEntriesAsync()
        {
            try
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
                    .Select(d => d.Name)
                    .ToList();

                int totalDrives = drives.Count;
                int currentDrive = 0;
                var allEntriesList = new List<JournalEntry>();

                foreach (var drive in drives)
                {
                    currentDrive++;
                    ExtractionStatus = $"Processing drive {drive} ({currentDrive}/{totalDrives})...";

                    UsnJournalInfo info = null;
                    await Task.Run(() => info = UsnJournalApi.QueryJournal(drive));
                    if (info == null)
                    {
                        continue;
                    }

                    var driveEntries = await Task.Run(() => JournalParser.ParseJournal(drive, (current, max, stage) =>
                    {
                        double driveProgress = (current / 100.0) / totalDrives;
                        double overallProgress = ((currentDrive - 1) / (double)totalDrives) + driveProgress;
                        int percent = (int)(overallProgress * 100);

                        if (percent > 100) percent = 100;
                        if (percent < 0) percent = 0;

                        JournalProgress = percent;
                        JournalProgressText = $"{percent}% - {stage}";
                        ExtractionStatus = stage;
                    }));

                    lock (allEntriesList)
                    {
                        allEntriesList.AddRange(driveEntries);
                    }

                    ExtractionStatus = $"Drive {drive}: {driveEntries.Count} entries. Total: {allEntriesList.Count}";
                }

                JournalProgress = 100;
                JournalProgressText = "100% - Journal Ready";
                ExtractionStatus = "Ready";

                JournalEntry[] sorted = null;
                Dictionary<string, List<JournalEntry>> reasonIndices = null;
                JournalEntry[] baseEntries = null;

                await Task.Run(() =>
                {
                    sorted = allEntriesList.OrderByDescending(e => e.FileTime).ToArray();
                    baseEntries = ApplyBaseFiltering(sorted);
                    reasonIndices = BuildReasonIndices(baseEntries);
                    
                    // Clear intermediate list - we now have our final sorted array
                    allEntriesList.Clear();
                    allEntriesList = null;
                });

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _allEntries = sorted;
                    _baseEntries = baseEntries;
                    _reasonIndices = reasonIndices;

                    var firstPageEntries = _baseEntries.Take(PageSize).ToList();
                    UsnEntries.Clear();
                    foreach (var entry in firstPageEntries)
                        UsnEntries.Add(entry);

                    ExtractionStatus = "Journal Ready";
                    _cachedFilteredEntries = _baseEntries.ToList();
                    OnPropertyChanged(nameof(FilteredEntriesCount));
                    OnPropertyChanged(nameof(TotalPages));
                    OnPropertyChanged(nameof(TotalEntries));
                    IsLoading = false;
                });
            }
            catch (Exception ex)
            {
                ExtractionStatus = $"Load failed: {ex.Message}";
                IsLoading = false;
            }
        }

        private void ShowInfo()
        {
            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
                .ToList();

            var infoLines = new List<string>();
            foreach (var drive in drives)
            {
                var info = UsnJournalApi.QueryJournal(drive.Name);
                if (info == null)
                {
                    infoLines.Add($"[ {drive.Name} ] - No USN found");
                    continue;
                }

                int entryCount = _allEntries?.Count(e => e.FullPath.StartsWith(drive.Name, StringComparison.OrdinalIgnoreCase)) ?? 0;

                long maxSizeMb = (long)(info.MaximumSize / (1024 * 1024));
                long allocDeltaMb = (long)(info.AllocationDelta / (1024 * 1024));

                string maxSizeNote = maxSizeMb > 32 ? " (Over Standard)" : maxSizeMb < 32 ? " (Under Standard)" : "";
                string allocDeltaNote = allocDeltaMb > 8 ? " (Over Standard)" : allocDeltaMb < 8 ? " (Under Standard)" : "";

                infoLines.Add($"USN Info [ {drive.Name} ] - {entryCount} Entries");
                infoLines.Add($"Max. Size:     {maxSizeMb} MB{maxSizeNote}");
                infoLines.Add($"Alloc. Delta:  {allocDeltaMb} MB{allocDeltaNote}");
                infoLines.Add("");
            }

            var message = string.Join(Environment.NewLine, infoLines);
            
            Vortex.UI.Views.PopupHelper.ShowTextPopup("Journal Info", message);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public int TotalEntries => _allEntries?.Length ?? 0;

        private static readonly HashSet<string> SpamExtensions = new HashSet<string>
        {
            ".pf", ".tmp", ".edb", ".cache", ".xcu", ".bak", ".old", 
            ".backup", ".swp", ".temp", ".partial", ".aux", ".core"
        };

        private static readonly HashSet<string> SystemPaths = new HashSet<string>
        {
            "system32", "syswow64", "appdata\\roaming", "appdata\\local"
        };

        private static readonly HashSet<string> AllowedSystemExts = new HashSet<string>
        {
            ".exe", ".dll"
        };

        private bool IsSpamlog(JournalEntry entry)
        {
            string ext = entry.GetFileExtension();
            string path = entry.GetLowerPath();
            
            foreach (var systemPath in SystemPaths)
            {
                if (path.Contains(systemPath))
                {
                    return !AllowedSystemExts.Contains("." + ext);
                }
            }
            
            return SpamExtensions.Contains("." + ext) || ext.StartsWith("log") || string.IsNullOrEmpty(ext);
        }

        public void StartLoading()
        {
            IsLoading = true;
            _totalLoadStopwatch = Stopwatch.StartNew();
            Task.Run(() => LoadEntriesAsync());
        }

        private double _journalProgress;
        public double JournalProgress
        {
            get => _journalProgress;
            set
            {
                if (_journalProgress != value)
                {
                    _journalProgress = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _journalProgressText = "0% - Waiting...";
        public string JournalProgressText
        {
            get => _journalProgressText;
            set
            {
                if (_journalProgressText != value)
                {
                    _journalProgressText = value;
                    OnPropertyChanged();
                }
            }
        }
    }
}