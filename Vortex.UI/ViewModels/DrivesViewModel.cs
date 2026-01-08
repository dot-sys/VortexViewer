using Drives.Core.Core;
using Drives.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Vortex.UI.ViewModels
{
    public class UsbDeviceViewModel
    {
        public string Timestamp { get; set; }
        public string DeviceName { get; set; }
        public string Label { get; set; }
        public string Drive { get; set; }
        public string BusType { get; set; }
        public string Serial { get; set; }
        public string VGUID { get; set; }
        public string Action { get; set; }
        public string Log { get; set; }
    }

    public class VolumeViewModel
    {
        public string Drive { get; set; }
        public string DiskNumber { get; set; }
        public string Label { get; set; }
        public string FileSystem { get; set; }
        public string Size { get; set; }
        public string BusType { get; set; }
        public string Serial { get; set; }
        public string PartitionID { get; set; }
        public string FormatDate { get; set; }
    }

    public class DeviceOverviewViewModel
    {
        public bool Connected { get; set; }
        public string DeviceName { get; set; }
        public string Label { get; set; }
        public string Drive { get; set; }
        public string BusType { get; set; }
        public string Serial { get; set; }
        public string UniqueIdentifier { get; set; }
    }

    public class DrivesViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<UsbDeviceViewModel> UsbEvents { get; } = new ObservableCollection<UsbDeviceViewModel>();
        public ObservableCollection<VolumeViewModel> ConnectedVolumes { get; } = new ObservableCollection<VolumeViewModel>();
        public ObservableCollection<DeviceOverviewViewModel> DeviceOverview { get; } = new ObservableCollection<DeviceOverviewViewModel>();

        // Full datasets for pagination
        private List<UsbDeviceViewModel> _allUsbEvents = new List<UsbDeviceViewModel>();
        private List<VolumeViewModel> _allConnectedVolumes = new List<VolumeViewModel>();
        private List<DeviceOverviewViewModel> _allDeviceOverview = new List<DeviceOverviewViewModel>();

        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        private bool _isOverviewTabSelected = true;
        public bool IsOverviewTabSelected
        {
            get => _isOverviewTabSelected;
            set
            {
                _isOverviewTabSelected = value;
                OnPropertyChanged();
                if (value) UpdatePage();
            }
        }

        private bool _isConnectedDrivesTabSelected = false;
        public bool IsConnectedDrivesTabSelected
        {
            get => _isConnectedDrivesTabSelected;
            set
            {
                _isConnectedDrivesTabSelected = value;
                OnPropertyChanged();
                if (value) UpdatePage();
            }
        }

        private bool _isTimelineTabSelected = false;
        public bool IsTimelineTabSelected
        {
            get => _isTimelineTabSelected;
            set
            {
                _isTimelineTabSelected = value;
                OnPropertyChanged();
                if (value) UpdatePage();
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

        public int PageSize { get; set; } = 21;

        public int TotalPages
        {
            get
            {
                int totalItems = GetCurrentDatasetCount();
                return totalItems > 0 ? (int)Math.Ceiling((double)totalItems / PageSize) : 1;
            }
        }

        public int FilteredEntriesCount => GetCurrentDatasetCount();

        public ICommand AnalyzeUsbDevicesCommand { get; }
        
        public ICommand SelectOverviewTabCommand { get; }
        public ICommand SelectConnectedDrivesTabCommand { get; }
        public ICommand SelectTimelineTabCommand { get; }

        public ICommand NextPageCommand { get; }
        public ICommand PrevPageCommand { get; }
        public ICommand Next5PagesCommand { get; }
        public ICommand Prev5PagesCommand { get; }
        public ICommand FirstPageCommand { get; }
        public ICommand LastPageCommand { get; }

        public DrivesViewModel()
        {
            AnalyzeUsbDevicesCommand = new SimpleRelayCommand(async _ => await AnalyzeUsbDevicesAsync());
            
            SelectOverviewTabCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = 1;
                IsOverviewTabSelected = true;
                IsConnectedDrivesTabSelected = false;
                IsTimelineTabSelected = false;
            });
            
            SelectConnectedDrivesTabCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = 1;
                IsOverviewTabSelected = false;
                IsConnectedDrivesTabSelected = true;
                IsTimelineTabSelected = false;
            });
            
            SelectTimelineTabCommand = new SimpleRelayCommand(_ =>
            {
                CurrentPage = 1;
                IsOverviewTabSelected = false;
                IsConnectedDrivesTabSelected = false;
                IsTimelineTabSelected = true;
            });

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

        public void StartAnalysis()
        {
            Task.Run(async () => await AnalyzeUsbDevicesAsync());
        }

        private int GetCurrentDatasetCount()
        {
            if (IsOverviewTabSelected)
                return _allDeviceOverview.Count;
            else if (IsConnectedDrivesTabSelected)
                return _allConnectedVolumes.Count;
            else if (IsTimelineTabSelected)
                return _allUsbEvents.Count;
            return 0;
        }

        private void UpdatePage()
        {
            if (IsLoading)
                return;

            if (IsOverviewTabSelected)
            {
                var pageEntries = _allDeviceOverview
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToList();

                DeviceOverview.Clear();
                foreach (var entry in pageEntries)
                    DeviceOverview.Add(entry);
            }
            else if (IsConnectedDrivesTabSelected)
            {
                var pageEntries = _allConnectedVolumes
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToList();

                ConnectedVolumes.Clear();
                foreach (var entry in pageEntries)
                    ConnectedVolumes.Add(entry);
            }
            else if (IsTimelineTabSelected)
            {
                var pageEntries = _allUsbEvents
                    .Skip((CurrentPage - 1) * PageSize)
                    .Take(PageSize)
                    .ToList();

                UsbEvents.Clear();
                foreach (var entry in pageEntries)
                    UsbEvents.Add(entry);
            }

            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(FilteredEntriesCount));
        }

        private async Task AnalyzeUsbDevicesAsync()
        {
            if (IsLoading) return;
            
            IsLoading = true;
            Status = "Analyzing USB devices and volumes...";

            try
            {
                UsbForensicsResult result = null;
                await Task.Run(() =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    
                    try
                    {
                        result = UsbForensicsAggregator.CollectUsbForensics();
                    }
                    finally
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                });

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _allUsbEvents.Clear();
                    _allConnectedVolumes.Clear();
                    _allDeviceOverview.Clear();

                    foreach (var evt in result.Events)
                    {
                        _allUsbEvents.Add(new UsbDeviceViewModel
                        {
                            Timestamp = evt.Timestamp?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                            DeviceName = evt.DeviceName ?? "",
                            Label = evt.Label ?? "",
                            Drive = evt.Drive ?? "",
                            BusType = evt.BusType ?? "",
                            Serial = evt.Serial ?? "",
                            VGUID = evt.VGUID ?? "",
                            Action = evt.Action ?? "",
                            Log = evt.Log ?? ""
                        });
                    }

                    foreach (var vol in result.Volumes)
                    {
                        _allConnectedVolumes.Add(new VolumeViewModel
                        {
                            Drive = vol.Drive ?? "",
                            DiskNumber = vol.DiskNumber.ToString(),
                            Label = vol.Label ?? "",
                            FileSystem = vol.FileSystem ?? "",
                            Size = vol.Size ?? "",
                            BusType = vol.BusType ?? "",
                            Serial = vol.Serial ?? "",
                            PartitionID = vol.PartitionID ?? "",
                            FormatDate = vol.FormatDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? ""
                        });
                    }

                    BuildDeviceOverview(result.Events, result.Volumes);
                    
                    // Clear intermediate result data - we now have our ViewModels
                    result.Events.Clear();
                    result.Volumes.Clear();
                    result = null;
                    
                    IsLoading = false;
                    CurrentPage = 1;
                    UpdatePage();
                    
                    OnPropertyChanged(nameof(TotalPages));
                    OnPropertyChanged(nameof(FilteredEntriesCount));
                    
                    Status = $"Ready - {_allDeviceOverview.Count} devices";
                });
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                IsLoading = false;
            }
        }

        private void BuildDeviceOverview(List<UsbDeviceEntry> events, List<VolumeInfo> volumes)
        {
            var timelineDevices = new Dictionary<string, DeviceOverviewViewModel>();

            foreach (var evt in events)
            {
                string uniqueKey = null;
                if (!string.IsNullOrWhiteSpace(evt.Serial))
                {
                    uniqueKey = $"SERIAL:{evt.Serial.Trim().ToUpperInvariant()}";
                }
                else if (!string.IsNullOrWhiteSpace(evt.VGUID))
                {
                    uniqueKey = $"VGUID:{evt.VGUID.Trim().ToLowerInvariant()}";
                }

                if (string.IsNullOrEmpty(uniqueKey))
                    continue;

                if (!timelineDevices.ContainsKey(uniqueKey))
                {
                    var overviewDevice = new DeviceOverviewViewModel
                    {
                        DeviceName = evt.DeviceName ?? "",
                        Label = evt.Label ?? "",
                        Drive = evt.Drive ?? "",
                        BusType = evt.BusType ?? "",
                        Serial = evt.Serial ?? "",
                        UniqueIdentifier = evt.VGUID ?? "",
                        Connected = false
                    };

                    timelineDevices[uniqueKey] = overviewDevice;
                }
                else
                {
                    var existing = timelineDevices[uniqueKey];

                    if (string.IsNullOrWhiteSpace(existing.DeviceName) && !string.IsNullOrWhiteSpace(evt.DeviceName))
                        existing.DeviceName = evt.DeviceName;

                    if (string.IsNullOrWhiteSpace(existing.Label) && !string.IsNullOrWhiteSpace(evt.Label))
                        existing.Label = evt.Label;

                    if (string.IsNullOrWhiteSpace(existing.Drive) && !string.IsNullOrWhiteSpace(evt.Drive))
                        existing.Drive = evt.Drive;

                    if (string.IsNullOrWhiteSpace(existing.BusType) && !string.IsNullOrWhiteSpace(evt.BusType))
                        existing.BusType = evt.BusType;

                    if (string.IsNullOrWhiteSpace(existing.Serial) && !string.IsNullOrWhiteSpace(evt.Serial))
                        existing.Serial = evt.Serial;

                    if (string.IsNullOrWhiteSpace(existing.UniqueIdentifier) && !string.IsNullOrWhiteSpace(evt.VGUID))
                        existing.UniqueIdentifier = evt.VGUID;
                }
            }

            var allOverviewDevices = new List<DeviceOverviewViewModel>();
            var processedVolumeKeys = new HashSet<string>();

            foreach (var volume in volumes)
            {
                string volumeKey = $"{volume.Drive}|{volume.PartitionID ?? ""}";

                if (processedVolumeKeys.Contains(volumeKey))
                    continue;

                processedVolumeKeys.Add(volumeKey);

                DeviceOverviewViewModel matchedTimelineDevice = null;

                foreach (var timelineDevice in timelineDevices.Values)
                {
                    bool serialMatch = !string.IsNullOrWhiteSpace(timelineDevice.Serial) &&
                                      !string.IsNullOrWhiteSpace(volume.Serial) &&
                                      timelineDevice.Serial.Trim().Equals(volume.Serial.Trim(), StringComparison.OrdinalIgnoreCase);

                    bool vguidMatch = !string.IsNullOrWhiteSpace(timelineDevice.UniqueIdentifier) &&
                                     !string.IsNullOrWhiteSpace(volume.PartitionID) &&
                                     timelineDevice.UniqueIdentifier.Trim().Equals(volume.PartitionID.Trim(), StringComparison.OrdinalIgnoreCase);

                    if (serialMatch || vguidMatch)
                    {
                        matchedTimelineDevice = timelineDevice;
                        break;
                    }
                }

                var connectedDevice = new DeviceOverviewViewModel
                {
                    Connected = true,
                    Drive = volume.Drive ?? "",
                    Label = volume.Label ?? "",
                    BusType = volume.BusType ?? "",
                    Serial = volume.Serial ?? "",
                    UniqueIdentifier = volume.PartitionID ?? "",
                    DeviceName = matchedTimelineDevice?.DeviceName ?? ""
                };

                allOverviewDevices.Add(connectedDevice);

                if (matchedTimelineDevice != null)
                {
                    matchedTimelineDevice.Connected = true;
                }
            }

            foreach (var timelineDevice in timelineDevices.Values)
            {
                if (timelineDevice.Connected)
                    continue;

                timelineDevice.Connected = false;
                allOverviewDevices.Add(timelineDevice);
            }

            var sortedDevices = allOverviewDevices
                .OrderByDescending(d => d.Connected ? 1 : 0)
                .ThenBy(d => string.IsNullOrWhiteSpace(d.Drive) ? 1 : 0)
                .ThenBy(d => d.Drive ?? "", StringComparer.OrdinalIgnoreCase);

            _allDeviceOverview.Clear();
            foreach (var device in sortedDevices)
            {
                _allDeviceOverview.Add(device);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class BooleanToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b
                ? new SolidColorBrush(Colors.LimeGreen)
                : new SolidColorBrush(Colors.Red);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotImplementedException();
    }
}
