using System;

// Hardware identification and component information models
namespace SysInfo.Core.Models
{
    // Stores unique hardware identifiers and specifications
    public class HardwareInfo
    {
        public string CpuModel { get; set; }
        public string CpuSerial { get; set; }
        public string GpuChipset { get; set; }
        public string GpuModel { get; set; }
        public string MotherboardModel { get; set; }
        public string MotherboardSerial { get; set; }
        public string BiosVendor { get; set; }
        public string BiosVersion { get; set; }
        public string BiosUuid { get; set; }
        public string HardDriveModel { get; set; }
        public string HardDriveSerial { get; set; }
        public string NetworkMacAddresses { get; set; }
    }
}
