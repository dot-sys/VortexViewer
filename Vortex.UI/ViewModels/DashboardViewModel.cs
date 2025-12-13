using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using SysInfo.Core.Models;
using SysInfo.Core.Util;

// ViewModel layer for UI binding
namespace Vortex.UI.ViewModels
{
    // ViewModel for dashboard system information display
    public class DashboardViewModel : INotifyPropertyChanged
    {
        // Collection of hardware information items
        public ObservableCollection<HardwareItem> HardwareItems { get; private set; }
        // Collection of software information items
        public ObservableCollection<SoftwareItem> SoftwareItems { get; private set; }
        // Collection of tampering detection items
        public ObservableCollection<TamperingItem> TamperingItems { get; private set; }

        // Backing field for loading status
        private bool _isLoading;
        // Indicates if data is loading
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

        // Initializes collections and loads system info
        public DashboardViewModel()
        {
            HardwareItems = new ObservableCollection<HardwareItem>();
            SoftwareItems = new ObservableCollection<SoftwareItem>();
            TamperingItems = new ObservableCollection<TamperingItem>();

            LoadSystemInfo();
        }

        // Asynchronously loads all system information
        private async void LoadSystemInfo()
        {
            IsLoading = true;

            await Task.Run(() =>
            {
                var hardware = HardwareParser.GetHardwareInfo();
                var software = SoftwareParser.GetSoftwareInfo();
                var tampering = TamperingParser.GetTamperingInfo();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    HardwareItems.Add(new HardwareItem { Property = "CPU Model", Value = hardware.CpuModel });
                    HardwareItems.Add(new HardwareItem { Property = "CPU Serial", Value = hardware.CpuSerial });
                    HardwareItems.Add(new HardwareItem { Property = "GPU Chipset", Value = hardware.GpuChipset });
                    HardwareItems.Add(new HardwareItem { Property = "GPU Name", Value = hardware.GpuModel });
                    HardwareItems.Add(new HardwareItem { Property = "Motherboard Model", Value = hardware.MotherboardModel });
                    HardwareItems.Add(new HardwareItem { Property = "Motherboard Serial", Value = hardware.MotherboardSerial });
                    HardwareItems.Add(new HardwareItem { Property = "BIOS Vendor", Value = hardware.BiosVendor });
                    HardwareItems.Add(new HardwareItem { Property = "BIOS Version", Value = hardware.BiosVersion });
                    HardwareItems.Add(new HardwareItem { Property = "BIOS UUID", Value = hardware.BiosUuid });
                    HardwareItems.Add(new HardwareItem { Property = "Hard Drive Model", Value = hardware.HardDriveModel });
                    HardwareItems.Add(new HardwareItem { Property = "Hard Drive Serial", Value = hardware.HardDriveSerial });
                    HardwareItems.Add(new HardwareItem { Property = "Network MAC Addresses", Value = hardware.NetworkMacAddresses });

                    SoftwareItems.Add(new SoftwareItem { Property = "Machine GUID", Value = software.MachineGuid });
                    SoftwareItems.Add(new SoftwareItem { Property = "Install Date", Value = software.InstallDate });
                    SoftwareItems.Add(new SoftwareItem { Property = "Windows Version", Value = software.WindowsVersion });
                    SoftwareItems.Add(new SoftwareItem { Property = "Windows Build", Value = software.WindowsBuild });
                    SoftwareItems.Add(new SoftwareItem { Property = "TPM 2.0 Vendor", Value = software.TpmVendor });
                    SoftwareItems.Add(new SoftwareItem { Property = "TPM 2.0 EK Public Key", Value = software.TpmEkPublicKey });
                    SoftwareItems.Add(new SoftwareItem { Property = "TPM 2.0 Short Key", Value = software.TpmShortKey });
                    SoftwareItems.Add(new SoftwareItem { Property = "Kernel DMA Protection", Value = software.KernelDmaProtection });
                    SoftwareItems.Add(new SoftwareItem { Property = "IOMMU Status", Value = software.IommuStatus });
                    SoftwareItems.Add(new SoftwareItem { Property = "Secure Boot Status", Value = software.SecureBootStatus });
                    SoftwareItems.Add(new SoftwareItem { Property = "Windows Defender Status", Value = software.WindowsDefenderStatus });
                    SoftwareItems.Add(new SoftwareItem { Property = "Defender Exclusions", Value = software.DefenderExclusions });

                    TamperingItems.Add(new TamperingItem { Property = "SRUM Created Date", Value = tampering.SrumCreatedDate });
                    TamperingItems.Add(new TamperingItem { Property = "AMCache Created Date", Value = tampering.AmCacheCreatedDate });
                    TamperingItems.Add(new TamperingItem { Property = "Defender EventLog Created Date", Value = tampering.DefenderEventLogCreatedDate });
                    TamperingItems.Add(new TamperingItem { Property = "Last Recycle Bin Deletion", Value = tampering.LastRecycleBinDeletion });
                    TamperingItems.Add(new TamperingItem { Property = "Volume Shadow Copies", Value = tampering.VolumeShadowCopies });
                    TamperingItems.Add(new TamperingItem { Property = "Oldest Prefetch File", Value = tampering.OldestPrefetchFile });
                    TamperingItems.Add(new TamperingItem { Property = "Prefetch Total Count", Value = tampering.PrefetchTotalCount });

                    IsLoading = false;
                });
            });
        }

        // Property changed event for binding
        public event PropertyChangedEventHandler PropertyChanged;
        // Raises property changed notification
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Represents hardware property value pair
    public class HardwareItem
    {
        // Hardware property name
        public string Property { get; set; }
        // Hardware property value
        public string Value { get; set; }
    }

    // Represents software property value pair
    public class SoftwareItem
    {
        // Software property name
        public string Property { get; set; }
        // Software property value
        public string Value { get; set; }
    }

    // Represents tampering indicator property pair
    public class TamperingItem
    {
        // Tampering property name
        public string Property { get; set; }
        // Tampering property value
        public string Value { get; set; }
    }
}
