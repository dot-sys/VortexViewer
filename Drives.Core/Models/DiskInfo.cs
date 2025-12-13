// Holds models for USB device data
namespace Drives.Core.Models
{
    // Physical disk metadata from WMI queries
    public class DiskInfo
    {
        public int DiskNumber { get; set; }
        public string BusType { get; set; }
        public string Serial { get; set; }

        public DiskInfo()
        {
        }

        public DiskInfo(int diskNumber, string busType, string serial)
        {
            DiskNumber = diskNumber;
            BusType = busType;
            Serial = serial;
        }
    }
}
