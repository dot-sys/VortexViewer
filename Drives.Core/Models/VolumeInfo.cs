using System;

// Holds models for USB device data
namespace Drives.Core.Models
{
    // Stores volume/drive metadata and properties
    public class VolumeInfo
    {
        public string Drive { get; set; }
        public int DiskNumber { get; set; }
        public string FileSystem { get; set; }
        public string Size { get; set; }
        public string BusType { get; set; }
        public string Serial { get; set; }
        public string PartitionID { get; set; }
        public string Label { get; set; }
        public DateTime? FormatDate { get; set; }
        public long SizeBytes { get; set; }

        public VolumeInfo()
        {
            Drive = string.Empty;
            FileSystem = string.Empty;
            Size = string.Empty;
            BusType = string.Empty;
            Serial = string.Empty;
            PartitionID = string.Empty;
            Label = string.Empty;
        }
    }
}
