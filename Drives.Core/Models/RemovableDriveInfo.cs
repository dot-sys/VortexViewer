// Holds models for USB device data
namespace Drives.Core.Models
{
    // Tracks connected removable drive metadata
    public class RemovableDriveInfo
    {
        public string DriveLetter { get; set; }
        public string VolumeGuid { get; set; }

        public RemovableDriveInfo()
        {
        }

        public RemovableDriveInfo(string driveLetter, string volumeGuid)
        {
            DriveLetter = driveLetter;
            VolumeGuid = volumeGuid;
        }
    }
}
