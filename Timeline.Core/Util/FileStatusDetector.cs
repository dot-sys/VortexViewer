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
            info.Status = string.Empty;
            return info;
        }

        if (!IsExecutableFile(filePath))
        {
            info.Status = string.Empty;
            return info;
        }

        if (!(fileExists ?? File.Exists(filePath)))
        {
            info.Status = "Invalid";
            return info;
        }

        // Signature validation with minimal exception handling
        // X509Certificate.Create.FromSignedFile throws CryptographicException even for
        // unsigned files, which floods debug output. We catch it silently.
        X509Certificate cert;
        
        try
        {
            cert = X509Certificate.CreateFromSignedFile(filePath);
        }
        catch (CryptographicException)
        {
            // File is not signed or has corrupted/invalid signature
            info.Status = "NotSigned";
            return info;
        }
        catch (UnauthorizedAccessException)
        {
            info.Status = "Unknown";
            return info;
        }
        catch (IOException)
        {
            info.Status = "Unknown";
            return info;
        }
        catch
        {
            info.Status = "Unknown";
            return info;
        }
        
        // cert is guaranteed to be assigned here if we didn't return early
        X509Certificate2 cert2 = null;
        
        try
        {
            cert2 = new X509Certificate2(cert);
            
            // Extract certificate fields
            info.CN = ExtractCertificateField(cert2.Subject, "CN");
            info.OU = ExtractCertificateField(cert2.Subject, "OU");
            info.S = ExtractCertificateField(cert2.Subject, "S");
            
            // Get serial number (first 8 chars)
            if (!string.IsNullOrEmpty(cert2.SerialNumber) && cert2.SerialNumber.Length > 8)
            {
                info.Serial = cert2.SerialNumber.Substring(0, 8);
            }
            else
            {
                info.Serial = cert2.SerialNumber ?? string.Empty;
            }
            
            // Validate certificate chain
            var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            
            bool isValid = false;
            try
            {
                isValid = chain.Build(cert2);
            }
            catch (CryptographicException)
            {
                // Chain validation failed
                isValid = false;
            }
            
            if (isValid)
            {
                info.Status = "Signed";
            }
            else
            {
                // Certificate exists but chain validation failed
                info.Status = "Invalid";
            }
        }
        catch (CryptographicException)
        {
            info.Status = "Invalid";
        }
        catch
        {
            info.Status = "Unknown";
        }
        finally
        {
            cert2?.Dispose();
        }
        
        return info;
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
            catch (System.Security.Cryptography.CryptographicException)
            {
                // CryptographicException thrown when accessing corrupted registry/shell bag data
                // On crypto exception, don't assume deleted - return false
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                // Permission denied - file may exist but we can't access it
                return false;
            }
            catch (IOException)
            {
                // I/O error (network timeout, device not ready, etc.)
                return false;
            }
            catch
            {
                // On exception (e.g., access denied), don't assume deleted
                // Return false to be conservative
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
        string pathStatus;
        
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
            catch (System.Security.Cryptography.CryptographicException)
            {
                pathStatus = "Unknown";
            }
            catch (UnauthorizedAccessException)
            {
                pathStatus = "Unknown";
            }
            catch (IOException)
            {
                pathStatus = "Unknown";
            }
            catch
            {
                pathStatus = "Unknown";
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
            signatureInfo = new SignatureInfo { Status = string.Empty };
            if (string.IsNullOrEmpty(modified))
            {
                modified = "Unknown";
            }
        }
        else
        {
            // File is present - perform actual signature extraction
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
