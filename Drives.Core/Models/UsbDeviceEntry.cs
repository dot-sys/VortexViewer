using System;

// Holds models for USB device data
namespace Drives.Core.Models
{
    // Represents single USB activity event
    public class UsbDeviceEntry
    {
        public DateTime? Timestamp { get; set; }
        public string DeviceName { get; set; }
        public string Label { get; set; }
        public string Drive { get; set; }
        public string BusType { get; set; }
        public string Serial { get; set; }
        public string VGUID { get; set; }
        public string Action { get; set; }
        public string Log { get; set; }

        public UsbDeviceEntry()
        {
            DeviceName = string.Empty;
            Label = string.Empty;
            Drive = string.Empty;
            BusType = string.Empty;
            Serial = string.Empty;
            VGUID = string.Empty;
            Action = string.Empty;
            Log = string.Empty;
        }

        public override string ToString()
        {
            return $"{Timestamp:yyyy-MM-dd HH:mm:ss}\t{DeviceName}\t{Label}\t{Drive}\t{BusType}\t{Serial}\t{VGUID}\t{Action}\t{Log}";
        }
    }
}
