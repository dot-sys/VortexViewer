using System.Collections.Generic;

// Holds models for USB device data
namespace Drives.Core.Models
{
    // Combines USB events and volume info
    public class UsbForensicsResult
    {
        public List<UsbDeviceEntry> Events { get; set; }
        public List<VolumeInfo> Volumes { get; set; }

        public UsbForensicsResult()
        {
            Events = new List<UsbDeviceEntry>();
            Volumes = new List<VolumeInfo>();
        }

        public UsbForensicsResult(List<UsbDeviceEntry> events, List<VolumeInfo> volumes)
        {
            Events = events ?? new List<UsbDeviceEntry>();
            Volumes = volumes ?? new List<VolumeInfo>();
        }
    }
}
