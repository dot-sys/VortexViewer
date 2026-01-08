using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Processes.Core.Models;
using Processes.Core.Util;
using System.Linq;
using System.Windows.Data;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Media;
using Timeline.Core.Util;

namespace Vortex.UI.ViewModels
{
    public class ProcessesViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ProcessInfo> Processes { get; } = new ObservableCollection<ProcessInfo>();
        public ObservableCollection<string> MemoryStrings { get; } = new ObservableCollection<string>();
        public ICollectionView ProcessesView { get; }

        private ProcessInfo _selectedProcess;
        public ProcessInfo SelectedProcess
        {
            get => _selectedProcess;
            set
            {
                if (_selectedProcess != value)
                {
                    _selectedProcess = value;
                    IsProcessProtected = false;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand RefreshProcessesCommand { get; }
        public ICommand GetStringsCommand { get; }
        public ICommand ShowUptimesCommand { get; }
        public ICommand GetPCACommand { get; }

        // Pagination properties
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
        public int TotalPages => (_activeStrings != null && _activeStrings.Count > 0)
            ? (int)System.Math.Ceiling((double)_activeStrings.Count / PageSize)
            : 1;
        public int FilteredEntriesCount => _activeStrings?.Count ?? 0;
        public int ExtractedStringsCount => _allStrings?.Count ?? 0;

        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }

        private List<string> _allStrings = new List<string>();

        private List<string> _activeStrings = new List<string>();

        // WinPathStringsFilter property
        private bool _winPathStringsFilter;
        public bool WinPathStringsFilter
        {
            get => _winPathStringsFilter;
            set
            {
                if (_winPathStringsFilter != value)
                {
                    _winPathStringsFilter = value;
                    OnPropertyChanged();
                    UpdateActiveStrings();
                }
            }
        }

        // UrlStringsFilter property
        private bool _urlStringsFilter;
        public bool UrlStringsFilter
        {
            get => _urlStringsFilter;
            set
            {
                if (_urlStringsFilter != value)
                {
                    _urlStringsFilter = value;
                    OnPropertyChanged();
                    UpdateActiveStrings();
                }
            }
        }

        private bool _combineProcesses = false;
        public bool CombineProcesses
        {
            get => _combineProcesses;
            set
            {
                if (_combineProcesses != value)
                {
                    _combineProcesses = value;
                    OnPropertyChanged();
                    LoadProcessesAsync();
                }
            }
        }

        private int _minLength = 4;
        public int MinLength
        {
            get => _minLength;
            set
            {
                if (_minLength != value)
                {
                    _minLength = value;
                    OnPropertyChanged();
                }
            }
        }

        private int? _maxLength;
        public int? MaxLength
        {
            get => _maxLength;
            set
            {
                if (_maxLength != value)
                {
                    _maxLength = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _unicodeStrings = true;
        public bool UnicodeStrings
        {
            get => _unicodeStrings;
            set
            {
                if (_unicodeStrings != value)
                {
                    _unicodeStrings = value;
                    OnPropertyChanged();
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
                    if (SelectedProcess != null) GetStrings();
                }
            }
        }

        private bool _reduceNonsenseStrings = true;
        public bool ReduceNonsenseStrings
        {
            get => _reduceNonsenseStrings;
            set
            {
                if (_reduceNonsenseStrings != value)
                {
                    _reduceNonsenseStrings = value;
                    OnPropertyChanged();
                    if (SelectedProcess != null) GetStrings();
                }
            }
        }

        private string _processFilterText;
        public string ProcessFilterText
        {
            get => _processFilterText;
            set
            {
                if (_processFilterText != value)
                {
                    _processFilterText = value;
                    OnPropertyChanged();
                    ProcessesView.Refresh();
                }
            }
        }

        private bool _isRegexSearch;
        public bool IsRegexSearch
        {
            get => _isRegexSearch;
            set
            {
                if (_isRegexSearch != value)
                {
                    _isRegexSearch = value;
                    if (value)
                    {
                        IsStartsWithSearch = false;
                        IsEndsWithSearch = false;
                    }
                    OnPropertyChanged();
                    UpdateActiveStrings();
                }
            }
        }

        private bool _isStartsWithSearch;
        public bool IsStartsWithSearch
        {
            get => _isStartsWithSearch;
            set
            {
                if (_isStartsWithSearch != value)
                {
                    _isStartsWithSearch = value;
                    if (value)
                    {
                        IsRegexSearch = false;
                        IsEndsWithSearch = false;
                    }
                    OnPropertyChanged();
                    UpdateActiveStrings();
                }
            }
        }

        private bool _isEndsWithSearch;
        public bool IsEndsWithSearch
        {
            get => _isEndsWithSearch;
            set
            {
                if (_isEndsWithSearch != value)
                {
                    _isEndsWithSearch = value;
                    if (value)
                    {
                        IsRegexSearch = false;
                        IsStartsWithSearch = false;
                    }
                    OnPropertyChanged();
                    UpdateActiveStrings();
                }
            }
        }


        private string _searchText;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    UpdateActiveStrings();
                }
            }
        }

        private bool _isLoadingStrings;
        public bool IsLoadingStrings
        {
            get => _isLoadingStrings;
            set
            {
                if (_isLoadingStrings != value)
                {
                    _isLoadingStrings = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isLoadingPCA;
        public bool IsLoadingPCA
        {
            get => _isLoadingPCA;
            set
            {
                if (_isLoadingPCA != value)
                {
                    _isLoadingPCA = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isProcessProtected;
        public bool IsProcessProtected
        {
            get => _isProcessProtected;
            set
            {
                if (_isProcessProtected != value)
                {
                    _isProcessProtected = value;
                    OnPropertyChanged();
                }
            }
        }

        private CancellationTokenSource _getStringsCts;

        public ProcessesViewModel()
        {
            RefreshProcessesCommand = new RelayCommand(RefreshProcesses);
            GetStringsCommand = new RelayCommand(GetStrings, () => SelectedProcess != null);
            ShowUptimesCommand = new RelayCommand(ShowUptimesPopup);
            GetPCACommand = new RelayCommand(GetPCAAsync);

            NextPageCommand = new RelayCommand(() =>
            {
                if (CurrentPage < TotalPages)
                    CurrentPage++;
            });
            PrevPageCommand = new RelayCommand(() =>
            {
                if (CurrentPage > 1)
                    CurrentPage--;
            });
            FirstPageCommand = new RelayCommand(() =>
            {
                CurrentPage = 1;
            });
            LastPageCommand = new RelayCommand(() =>
            {
                CurrentPage = TotalPages;
            });

            ProcessesView = CollectionViewSource.GetDefaultView(Processes);
            ProcessesView.Filter = FilterProcesses;

            LoadProcessesAsync();
        }

        private void RefreshProcesses()
        {
            Processes.Clear();
            var all = ReadProcesses.GetRunningProcesses();

            foreach (var proc in all
                .GroupBy(p => new { p.Name, p.Id })
                .Select(g => g.First())
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                Processes.Add(proc);
            }
        }

        private async void GetStrings()
        {
            if (SelectedProcess == null) return;

            _getStringsCts?.Cancel();
            _getStringsCts = new CancellationTokenSource();
            var cancellationToken = _getStringsCts.Token;

            if (CombineProcesses && SelectedProcess is CombinedProcessInfo && !WinPathStringsFilter && !UrlStringsFilter)
            {
                SystemSounds.Hand.Play();
                MessageBox.Show(
                    "When 'Combine Processes' is active, you must enable either 'Win Path Strings' and/or 'URL Strings'.",
                    "Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            IsLoadingStrings = true;
            IsProcessProtected = false;
            MemoryStrings.Clear();
            _allStrings.Clear();
            OnPropertyChanged(nameof(ExtractedStringsCount));

            try
            {
                List<string> strings = new List<string>();

                if (CombineProcesses && SelectedProcess is CombinedProcessInfo combined)
                {
                    var tasks = combined.Ids.Select(pid =>
                        ReadMemory.GetStringsFromProcessAsync(pid, MinLength, !UnicodeStrings, cancellationToken)
                    ).ToArray();

                    var results = await Task.WhenAll(tasks);

                    foreach (var result in results)
                    {
                        strings.AddRange(result.Strings);
                    }
                }
                else
                {
                    var result = await ReadMemory.GetStringsFromProcessAsync(SelectedProcess.Id, MinLength, !UnicodeStrings, cancellationToken);
                    strings = result.Strings;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (ReduceDuplicates)
                    strings = strings.Distinct().ToList();

                if (ReduceNonsenseStrings)
                    strings = FilterNonsenseStrings(strings);

                _allStrings = strings;
                UpdateActiveStrings();

                OnPropertyChanged(nameof(ExtractedStringsCount));
            }
            catch (OperationCanceledException)
            {
                _allStrings.Clear();
                _activeStrings.Clear();
                MemoryStrings.Clear();
                OnPropertyChanged(nameof(ExtractedStringsCount));
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 5 || ex.NativeErrorCode == 0x5)
                {
                    IsProcessProtected = true;
                    _allStrings.Clear();
                    _activeStrings.Clear();
                    MemoryStrings.Clear();
                    OnPropertyChanged(nameof(ExtractedStringsCount));
                    
                    await Task.Delay(3000);
                    IsProcessProtected = false;
                    return;
                }
                else
                {
                    MessageBox.Show($"Failed to access process: {ex.Message} (Error Code: {ex.NativeErrorCode})", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while reading memory: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoadingStrings = false;
            }
        }

        private List<string> FilterNonsenseStrings(List<string> input)
        {
            return input;
        }

        private void UpdatePage()
        {
            MemoryStrings.Clear();
            if (_activeStrings == null) return;
            var pageEntries = _activeStrings
                .Skip((CurrentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();
            foreach (var s in pageEntries)
                MemoryStrings.Add(s);
            OnPropertyChanged(nameof(FilteredEntriesCount));
            OnPropertyChanged(nameof(TotalPages));
        }

        private async void LoadProcessesAsync()
        {
            var processList = await System.Threading.Tasks.Task.Run(() =>
                ReadProcesses.GetRunningProcesses()
            );

            Processes.Clear();

            if (CombineProcesses)
            {
                var combined = processList
                    .GroupBy(p => p.Name)
                    .OrderBy(g => g.Key, System.StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var ids = g.Select(p => p.Id).ToList();
                        var name = g.Key;
                        var idsString = string.Join(", ", ids);
                        var display = $"{name} ({idsString})";
                        // Set your desired max display length here
                        const int MaxDisplayLength = 40;

                        if (display.Length > MaxDisplayLength)
                        {
                            var shownIds = new List<int>();
                            int currentLength = name.Length + 3; // " (" and ")"
                            foreach (var id in ids)
                            {
                                var idStr = id.ToString();
                                // Reserve space for ", ... )" (7 chars)
                                if (currentLength + idStr.Length + 2 + 7 > MaxDisplayLength)
                                    break;
                                shownIds.Add(id);
                                currentLength += idStr.Length + 2; // ", "
                            }
                            var shownIdsString = string.Join(", ", shownIds);
                            display = $"{name} ({shownIdsString}, ... )";
                            // If still too long (edge case), cut to max length
                            if (display.Length > MaxDisplayLength)
                                display = display.Substring(0, MaxDisplayLength - 1) + "�";
                        }
                        return new CombinedProcessInfo(name, ids, display);
                    })
                    .ToList();

                foreach (var proc in combined)
                    Processes.Add(proc);
            }
            else
            {
                foreach (var proc in processList.OrderBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase))
                    Processes.Add(proc);
            }
        }

        private bool FilterProcesses(object obj)
        {
            if (string.IsNullOrEmpty(ProcessFilterText))
                return true;
            return obj is ProcessInfo process && process.ToString().IndexOf(ProcessFilterText, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateActiveStrings()
        {
            IEnumerable<string> baseSet;
            if (!WinPathStringsFilter && !UrlStringsFilter)
            {
                baseSet = _allStrings;
            }
            else
            {
                var winPaths = WinPathStringsFilter ? _allStrings.Where(s => WinPathRegex.IsMatch(s)) : Enumerable.Empty<string>();
                var urls = UrlStringsFilter ? _allStrings.Where(s => UrlRegex.IsMatch(s)) : Enumerable.Empty<string>();
                
                if (WinPathStringsFilter && UrlStringsFilter)
                    baseSet = winPaths.Concat(urls).Distinct();
                else if (WinPathStringsFilter)
                    baseSet = winPaths;
                else
                    baseSet = urls;
            }

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                if (IsRegexSearch)
                {
                    try
                    {
                        var regex = new Regex(SearchText, RegexOptions.IgnoreCase);
                        baseSet = baseSet.Where(s => regex.IsMatch(s));
                    }
                    catch
                    {
                        baseSet = Enumerable.Empty<string>();
                    }
                }
                else if (IsStartsWithSearch)
                {
                    baseSet = baseSet.Where(s => s.StartsWith(SearchText, StringComparison.OrdinalIgnoreCase));
                }
                else if (IsEndsWithSearch)
                {
                    baseSet = baseSet.Where(s => s.EndsWith(SearchText, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    baseSet = baseSet.Where(s => s.IndexOf(SearchText, System.StringComparison.OrdinalIgnoreCase) >= 0);
                }
            }

            _activeStrings = baseSet.ToList();
            CurrentPage = 1;
            UpdatePage();
        }

        private void ShowUptimesPopup()
        {
            var uptimes = ProcessUptimeCollector.GetTargetProcessUptimes();
            var message = string.Join(Environment.NewLine, uptimes.Select(u => $"{u.Name,-12} {u.Uptime}"));
            SystemSounds.Beep.Play();
            
            Vortex.UI.Views.PopupHelper.ShowTextPopup("Process Uptimes", message);
        }

        private async void GetPCAAsync()
        {
            try
            {
                IsLoadingPCA = true;

                var extractedPaths = await PCAExtractor.ExtractPCATracesAsync(null, CancellationToken.None);

                if (extractedPaths.Count == 0)
                {
                    IsLoadingPCA = false;
                    MessageBox.Show(
                        "No PCA trace strings found in target processes.",
                        "No Results",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    return;
                }

                var pcaResults = new List<PCAResult>();
                var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (path, source) in extractedPaths)
                {
                    if (!processedPaths.Add(path))
                        continue;

                    var (modified, signatureInfo, pathStatus) = FileStatusDetector.AnalyzeFile(path);

                    var modifiedStatus = pathStatus;
                    if (!string.IsNullOrEmpty(modified))
                    {
                        modifiedStatus = $"{modified}, {pathStatus}";
                    }

                    pcaResults.Add(new PCAResult(
                        path: path,
                        modified: modifiedStatus,
                        signed: signatureInfo.Status,
                        source: source
                    ));
                }

                IsLoadingPCA = false;

                SystemSounds.Beep.Play();
                Vortex.UI.Views.PopupHelper.ShowDataGridPopup("PCA Analysis Results", pcaResults);
            }
            catch (Exception ex)
            {
                IsLoadingPCA = false;
                MessageBox.Show(
                    $"Error during PCA extraction: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private static readonly Regex WinPathRegex =
            new Regex(@"^(?:""?[a-zA-Z]\:|\\\\[^\\\/\:\*\?\<\>\|]+\\[^\\\/\:\*\?\<\>\|]*)\\(?:[^\\\/\:\*\?\<\>\|]+\\)*\w([^\\\/\:\*\?\<\>\|])*", RegexOptions.Compiled);

        private static readonly Regex UrlRegex =
            new Regex(@"^[a-z][a-z0-9+\-.]*://([a-z0-9\-._~%!$&'()*+,;=]+@)?(?<host>[a-z0-9\-._~%]+|\[[a-f0-9:.]+\]|\[v[a-f0-9][a-z0-9\-._~%!$&'()*+,;=:]+\])(:[0-9]+)?(/[a-z0-9\-._~%!$&'()*+,;=:@]+)*/?(\?[a-z0-9\-._~%!$&'()*+,;=:@/?]*)?(\#[a-z0-9\-._~%!$&'()*+,;=:@/?]*)?$", RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace | RegexOptions.IgnoreCase);

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
