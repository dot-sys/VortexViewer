using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// File status and signature detection
namespace Timeline.Core.Util
{
    // Contains digital signature certificate information
    public class SignatureInfo
    {
        // Signature validation status
        public string Status { get; set; }
        // Certificate common name
        public string CN { get; set; }
        // Certificate organizational unit
        public string OU { get; set; }
        // Certificate state or province
        public string S { get; set; }
        // Certificate serial number prefix
        public string Serial { get; set; }
        
        public SignatureInfo()
        {
            Status = "Unknown";
            CN = string.Empty;
            OU = string.Empty;
            S = string.Empty;
            Serial = string.Empty;
        }
    }

    // Detects file status and signatures
    public static class FileStatusDetector
    {
        public static string DetectModificationStatus(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "Unknown";

            if (IsRenamedPath(filePath))
                return "Renamed";

            if (IsUnknownPath(filePath))
                return "Unknown";

            if (IsDeletedFile(filePath))
                return "Deleted";

            return string.Empty;
        }

    /// <summary>
    /// Extracts detailed signature information from a file.
    /// </summary>
    /// <param name="filePath">The file path to check</param>
    /// <param name="fileExists">Pre-checked file existence to avoid redundant I/O</param>
    /// <returns>SignatureInfo object with certificate details</returns>
    public static SignatureInfo ExtractSignatureInfo(string filePath, bool? fileExists = null)
    {
        var info = new SignatureInfo();
        
        if (string.IsNullOrEmpty(filePath))
        {
            info.Status = "Invalid";
            return info;
        }

        if (!IsExecutableFile(filePath))
        {
            info.Status = string.Empty;
            return info;
        }

        bool exists = fileExists ?? File.Exists(filePath);
        if (!exists)
        {
            info.Status = "Invalid";
            return info;
        }

            try
            {
                X509Certificate2 cert = null;
                
                try
                {
                    cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
                }
                catch (CryptographicException)
                {
                    // Malformed or corrupted certificate data
                    info.Status = "NotSigned";
                    return info;
                }
                catch
                {
                    info.Status = "NotSigned";
                    return info;
                }

                if (cert == null)
                {
                    info.Status = "NotSigned";
                    return info;
                }

                try
                {
                    var chain = new X509Chain();
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    bool isValid = chain.Build(cert);
                    
                    info.Status = isValid ? "Valid" : "Invalid";

                    var subject = cert.Subject;
                    info.CN = ExtractCertificateField(subject, "CN");
                    info.OU = ExtractCertificateField(subject, "OU");
                    info.S = ExtractCertificateField(subject, "S");
                    
                    var serialNumber = cert.SerialNumber;
                    if (!string.IsNullOrEmpty(serialNumber) && serialNumber.Length >= 5)
                    {
                        info.Serial = serialNumber.Substring(0, 5);
                    }
                    else
                    {
                        info.Serial = serialNumber ?? string.Empty;
                    }
                }
                catch (CryptographicException)
                {
                    // Error building certificate chain or accessing certificate properties
                    info.Status = "Invalid";
                    return info;
                }
                finally
                {
                    cert?.Dispose();
                }
                
                return info;
            }
            catch (CryptographicException)
            {
                // Top-level crypto exception handler
                info.Status = "Unknown";
                return info;
            }
            catch
            {
                info.Status = "Unknown";
                return info;
            }
        }
        
        private static string ExtractCertificateField(string subject, string fieldName)
        {
            if (string.IsNullOrEmpty(subject))
                return string.Empty;
                
            try
            {
                var parts = subject.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed.StartsWith(fieldName + "=", StringComparison.OrdinalIgnoreCase))
                    {
                        return trimmed.Substring(fieldName.Length + 1).Trim();
                    }
                }
            }
            catch
            {
            }
            
            return string.Empty;
        }

        private static bool IsRenamedPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (path.IndexOf("renamed", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (path.IndexOf(".old", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var fileName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(fileName))
            {
                var dotCount = 0;
                foreach (var c in fileName)
                {
                    if (c == '.') dotCount++;
                }
                
                if (dotCount > 1)
                {
                    var ext = Path.GetExtension(path);
                    if (ext.Equals(".bak", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".old", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".backup", StringComparison.OrdinalIgnoreCase) ||
                        ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsUnknownPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            if (path.StartsWith(":"))
                return true;

            if (path.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (path.StartsWith("["))
                return true;

            if (path.IndexOf("Unmapped", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (path.IndexOf("MISSING", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (path.Length >= 3)
            {
                if (!((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')))
                {
                    if (!path.StartsWith(@"\\"))
                    {
                        return true;
                    }
                }
                else if (path[1] != ':' || path[2] != '\\')
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsDeletedFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            if (path.StartsWith("HKEY", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("HK", StringComparison.OrdinalIgnoreCase))
                return false;

            if (IsUnknownPath(path))
                return false;

            if (path.Length < 3)
                return false;

            bool isValidPath = false;
            
            // Check for drive letter path (C:\...)
            if ((path[0] >= 'A' && path[0] <= 'Z' || path[0] >= 'a' && path[0] <= 'z') && 
                path[1] == ':' && path[2] == '\\')
            {
                isValidPath = true;
            }
            
            if (path.StartsWith(@"\\"))
            {
                isValidPath = true;
            }

            if (!isValidPath)
                return false;

            try
            {
                if (File.Exists(path))
                    return false;

                if (Directory.Exists(path))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsExecutableFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext))
                return false;

            return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".sys", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".ocx", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".scr", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".cpl", StringComparison.OrdinalIgnoreCase);
        }

    public static (string Modified, SignatureInfo SignatureInfo, string PathStatus) AnalyzeFile(string filePath)
    {
        var modified = DetectModificationStatus(filePath);
        
        bool fileExists = false;
        string pathStatus = null;
        
        if (!string.IsNullOrEmpty(filePath))
        {
            try
            {
                if (File.Exists(filePath))
                {
                    fileExists = true;
                    pathStatus = "Present";
                }
                else if (Directory.Exists(filePath))
                {
                    pathStatus = "Present";
                }
                else if (!IsUnknownPath(filePath) && IsValidWindowsPath(filePath))
                {
                    pathStatus = "Deleted";
                }
                else
                {
                    pathStatus = "Unknown";
                }
            }
            catch
            {
                pathStatus = "Deleted";
            }
        }
        else
        {
            pathStatus = "Unknown";
        }
        
        SignatureInfo signatureInfo;
        if (pathStatus == "Deleted")
        {
            signatureInfo = new SignatureInfo { Status = "Invalid" };
            modified = "Deleted";
        }
        else if (pathStatus == "Unknown")
        {
            signatureInfo = new SignatureInfo { Status = "Invalid" };
            modified = "Unknown";
        }
        else
        {
            signatureInfo = ExtractSignatureInfo(filePath, fileExists);
        }
        
        return (modified, signatureInfo, pathStatus);
    }
    
    private static bool IsValidWindowsPath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 3)
            return false;
            
        // Check for drive letter path (C:\...)
        if ((path[0] >= 'A' && path[0] <= 'Z' || path[0] >= 'a' && path[0] <= 'z') && 
            path[1] == ':' && path[2] == '\\')
        {
            return true;
        }
        
        if (path.StartsWith(@"\\\\"))
        {
            return true;
        }
        
        return false;
    }
    }
}
