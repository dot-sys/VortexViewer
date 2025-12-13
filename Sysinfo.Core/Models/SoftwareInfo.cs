using System;

// Software configuration and security status models
namespace SysInfo.Core.Models
{
    // Stores Windows installation and security settings
    public class SoftwareInfo
    {
        public string MachineGuid { get; set; }
        public string InstallDate { get; set; }
        public string WindowsVersion { get; set; }
        public string WindowsBuild { get; set; }
        public string TpmVendor { get; set; }
        public string TpmEkPublicKey { get; set; }
        public string TpmShortKey { get; set; }
        public string KernelDmaProtection { get; set; }
        public string IommuStatus { get; set; }
        public string SecureBootStatus { get; set; }
        public string WindowsDefenderStatus { get; set; }
        public string DefenderExclusions { get; set; }
    }
}
