using System;
using System.Linq;
using Microsoft.Win32;
using SysInfo.Core.Models;
using Tpm2Lib;

// Software and security configuration extraction utilities
namespace SysInfo.Core.Util
{
    // Extracts Windows and TPM configuration details
    public static class SoftwareParser
    {
        private const string WindowsNtCurrentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        private const string DefenderRoot = @"SOFTWARE\Microsoft\Windows Defender";
        private const string DefenderExclusionsRoot = @"SOFTWARE\Microsoft\Windows Defender\Exclusions";

        // Collects all software configuration into single object
        public static SoftwareInfo GetSoftwareInfo()
        {
            var info = new SoftwareInfo();

            try
            {
                info.MachineGuid = GetMachineGuid();
                info.InstallDate = GetInstallDate();
                info.WindowsVersion = GetWindowsVersion();
                info.WindowsBuild = GetWindowsBuild();
                info.TpmVendor = GetTpmVendor();
                info.TpmEkPublicKey = GetTpmEkPublicKey();
                info.TpmShortKey = GetTpmShortKey(info.TpmEkPublicKey);
                info.KernelDmaProtection = GetKernelDmaProtection();
                info.IommuStatus = GetIommuStatus();
                info.SecureBootStatus = GetSecureBootStatus();
                info.WindowsDefenderStatus = GetWindowsDefenderStatus();
                info.DefenderExclusions = GetDefenderExclusions();
            }
            catch (Exception ex)
            {
                info.MachineGuid = $"Error: {ex.Message}";
            }

            return info;
        }

        // Safely reads registry value with error handling
        private static string GetRegistryValue(string keyPath, string valueName, string defaultValue = "Unavailable")
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(keyPath))
                {
                    var value = key?.GetValue(valueName)?.ToString();
                    return string.IsNullOrWhiteSpace(value) ? defaultValue : value;
                }
            }
            catch
            {
                return defaultValue;
            }
        }

        // Retrieves unique Windows machine GUID identifier
        private static string GetMachineGuid()
        {
            return GetRegistryValue(@"SOFTWARE\Microsoft\Cryptography", "MachineGuid");
        }

        // Converts Windows install date from Unix timestamp
        private static string GetInstallDate()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(WindowsNtCurrentVersion))
                {
                    var installDate = key?.GetValue("InstallDate");
                    if (installDate != null)
                    {
                        var timestamp = Convert.ToInt64(installDate);
                        var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
                        return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Returns formatted Windows version and edition name
        private static string GetWindowsVersion()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(WindowsNtCurrentVersion))
                {
                    var productName = key?.GetValue("ProductName")?.ToString();
                    var displayVersion = key?.GetValue("DisplayVersion")?.ToString();
                    var currentBuild = key?.GetValue("CurrentBuild")?.ToString();
                    
                    if (!string.IsNullOrWhiteSpace(currentBuild) && int.TryParse(currentBuild, out int buildNumber))
                    {
                        if (buildNumber >= 22000 && !string.IsNullOrWhiteSpace(productName))
                            productName = productName.Replace("Windows 10", "Windows 11");
                    }
                    
                    if (!string.IsNullOrWhiteSpace(productName))
                    {
                        if (!string.IsNullOrWhiteSpace(displayVersion))
                            return $"{productName} ({displayVersion})";
                        return productName;
                    }
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Returns Windows build number with revision
        private static string GetWindowsBuild()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(WindowsNtCurrentVersion))
                {
                    var currentBuild = key?.GetValue("CurrentBuild")?.ToString();
                    var ubr = key?.GetValue("UBR")?.ToString();
                    
                    if (!string.IsNullOrWhiteSpace(currentBuild))
                    {
                        if (!string.IsNullOrWhiteSpace(ubr))
                            return $"Build {currentBuild}.{ubr}";
                        return $"Build {currentBuild}";
                    }
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Queries TPM chip manufacturer via TPM interface
        private static string GetTpmVendor()
        {
            try
            {
                using (var tpmDevice = new TbsDevice())
                {
                    tpmDevice.Connect();
                    var tpm = new Tpm2(tpmDevice);
                    
                    ICapabilitiesUnion capData;
                    tpm.GetCapability(Cap.TpmProperties, (uint)Pt.Manufacturer, 1, out capData);
                    
                    if (capData is TaggedTpmPropertyArray propArray && 
                        propArray.tpmProperty != null && 
                        propArray.tpmProperty.Length > 0)
                    {
                        var vendorBytes = BitConverter.GetBytes(propArray.tpmProperty[0].value);
                        Array.Reverse(vendorBytes);
                        var vendorString = System.Text.Encoding.ASCII.GetString(vendorBytes).Trim('\0');
                        tpmDevice.Dispose();
                        return vendorString;
                    }
                    
                    tpmDevice.Dispose();
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Reads TPM endorsement key public portion
        private static string GetTpmEkPublicKey()
        {
            try
            {
                using (var tpmDevice = new TbsDevice())
                {
                    tpmDevice.Connect();
                    var tpm = new Tpm2(tpmDevice);
                    
                    var ekHandle = new TpmHandle(0x81010001);
                    var ekPub = tpm.ReadPublic(ekHandle, out _, out _);
                    
                    if (ekPub?.unique is Tpm2bPublicKeyRsa rsa)
                    {
                        var tpmId = BitConverter.ToString(rsa.buffer).Replace("-", "");
                        tpmDevice.Dispose();
                        return tpmId;
                    }
                    
                    tpmDevice.Dispose();
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Creates shortened TPM key from specific indices
        private static string GetTpmShortKey(string tpmEkPublicKey)
        {
            if (string.IsNullOrWhiteSpace(tpmEkPublicKey) || tpmEkPublicKey == "Unavailable" || tpmEkPublicKey.Length < 512)
                return "Unavailable";

            try
            {
                var indices = new int[] { 0, 64, 128, 192, 256, 320, 384, 448, 511 };
                var shortKey = new System.Text.StringBuilder();

                foreach (var index in indices)
                {
                    if (index < tpmEkPublicKey.Length)
                        shortKey.Append(tpmEkPublicKey[index]);
                }

                if (tpmEkPublicKey.Length >= 512)
                    shortKey.Append(tpmEkPublicKey[tpmEkPublicKey.Length - 1]);

                return shortKey.ToString();
            }
            catch
            {
                return "Unavailable";
            }
        }

        // Checks kernel DMA protection availability and status
        private static string GetKernelDmaProtection()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT * FROM Win32_DeviceGuard"))
                using (var collection = searcher.Get())
                {
                    var obj = collection.Cast<System.Management.ManagementObject>().FirstOrDefault();
                    if (obj == null)
                        return "Unavailable on this system";

                    var available = (uint[])obj["AvailableSecurityProperties"];
                    var running = (uint[])obj["SecurityServicesRunning"];

                    if (available == null || running == null)
                        return "Unavailable on this system";

                    bool dmaSupported = available.Contains<uint>(5);
                    bool dmaEnabled = running.Contains<uint>(5);

                    string status = dmaEnabled ? "Enabled" : "Disabled";
                    string supportStatus = dmaSupported ? "Supported" : "Not Supported";

                    return $"{status} ({supportStatus})";
                }
            }
            catch
            {
                return "Unavailable on this system";
            }
        }

        // Verifies IOMMU hardware virtualization enabled status
        private static string GetIommuStatus()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("root\\wmi", "SELECT * FROM MSFT_HardwareDmaPolicy"))
                using (var collection = searcher.Get())
                {
                    foreach (System.Management.ManagementObject obj in collection)
                    {
                        var dmaRemapping = obj["DmaRemapping"];
                        if (dmaRemapping != null)
                        {
                            bool enabled = Convert.ToBoolean(dmaRemapping);
                            return enabled ? "Enabled" : "Disabled";
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard"))
                {
                    var enabled = key?.GetValue("EnableVirtualizationBasedSecurity");
                    if (enabled != null)
                        return Convert.ToInt32(enabled) == 1 ? "Enabled" : "Disabled";
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Checks UEFI Secure Boot enabled state
        private static string GetSecureBootStatus()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State"))
                {
                    var enabled = key?.GetValue("UEFISecureBootEnabled");
                    if (enabled != null)
                        return Convert.ToInt32(enabled) == 1 ? "Enabled" : "Disabled";
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Determines Windows Defender antivirus enabled state
        private static string GetWindowsDefenderStatus()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(DefenderRoot))
                {
                    var disableAntiSpyware = key?.GetValue("DisableAntiSpyware");
                    if (disableAntiSpyware != null)
                        return Convert.ToInt32(disableAntiSpyware) == 1 ? "Disabled" : "Enabled";
                }

                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows Defender"))
                {
                    var disableAntiSpyware = key?.GetValue("DisableAntiSpyware");
                    if (disableAntiSpyware != null)
                        return Convert.ToInt32(disableAntiSpyware) == 1 ? "Disabled" : "Enabled";
                }

                using (var searcher = new System.Management.ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus"))
                using (var collection = searcher.Get())
                {
                    var obj = collection.Cast<System.Management.ManagementObject>().FirstOrDefault();
                    if (obj != null)
                    {
                        var antivirusEnabled = obj["AntivirusEnabled"];
                        if (antivirusEnabled != null)
                            return Convert.ToBoolean(antivirusEnabled) ? "Enabled" : "Disabled";
                    }
                }
            }
            catch
            {
            }

            return "Unavailable";
        }

        // Lists all Windows Defender exclusion paths
        private static string GetDefenderExclusions()
        {
            var exclusions = new System.Collections.Generic.List<string>();

            try
            {
                var exclusionPaths = new[]
                {
                    $@"{DefenderExclusionsRoot}\Paths",
                    $@"{DefenderExclusionsRoot}\Extensions",
                    $@"{DefenderExclusionsRoot}\Processes"
                };

                foreach (var path in exclusionPaths)
                {
                    try
                    {
                        using (var key = Registry.LocalMachine.OpenSubKey(path))
                        {
                            if (key != null)
                            {
                                var valueNames = key.GetValueNames();
                                foreach (var valueName in valueNames)
                                {
                                    if (!string.IsNullOrWhiteSpace(valueName))
                                        exclusions.Add(valueName);
                                }
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                try
                {
                    using (var searcher = new System.Management.ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpPreference"))
                    using (var collection = searcher.Get())
                    {
                        var obj = collection.Cast<System.Management.ManagementObject>().FirstOrDefault();
                        if (obj != null)
                        {
                            AddExclusionsFromArray(exclusions, obj["ExclusionPath"] as string[]);
                            AddExclusionsFromArray(exclusions, obj["ExclusionExtension"] as string[]);
                            AddExclusionsFromArray(exclusions, obj["ExclusionProcess"] as string[]);
                        }
                    }
                }
                catch
                {
                }
            }
            catch
            {
            }

            return exclusions.Count == 0 ? "None" : string.Join(", ", exclusions);
        }

        // Adds unique exclusion items to exclusions list
        private static void AddExclusionsFromArray(System.Collections.Generic.List<string> exclusions, string[] items)
        {
            if (items == null)
                return;

            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item) && !exclusions.Contains(item))
                    exclusions.Add(item);
            }
        }
    }
}
