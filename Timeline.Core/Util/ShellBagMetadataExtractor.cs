using System;
using System.IO;
using System.Linq;
using Registry.Abstractions;
using ExtensionBlocks;

// Shellbag metadata structure and extractor
namespace Timeline.Core.Util
{
    // Holds parsed shellbag data
    public class ShellBagMetadata
    {
        public int ShellbagNumber { get; set; }
        public string ParentPath { get; set; }
        public string OutputPath { get; set; }
        public DateTime? LastWriteTime { get; set; }
        public DateTime? CreatedOnTime { get; set; }
        public DateTime? LastModificationTime { get; set; }
        public ulong? MFTEntryNumber { get; set; }
        public int? MFTSequenceNumber { get; set; }
        public string BagPath { get; set; }
        public bool RegIsDeleted { get; set; }
        public bool IsValid { get; set; }
        public string AbsolutePath { get; set; }
        public bool FolderIsDeleted { get; set; }
    }

    // Extracts metadata from shellbag binary data
    public static class ShellBagMetadataExtractor
    {
        // Prevents corrupted timestamps like year 2075
        private static DateTime? ValidateTimestamp(DateTime? timestamp)
        {
            if (!timestamp.HasValue)
                return null;
                
            var dt = timestamp.Value;
            
            if (dt.Year < 1980 || dt.Year > 2050)
                return null;
                
            return timestamp;
        }

        private static bool CheckFolderExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                return Directory.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        public static ShellBagMetadata ExtractMetadata(byte[] rawBytes, RegistryKey parentKey, string valueName, string parentPath, int shellbagNumber)
        {
            var metadata = new ShellBagMetadata
            {
                ShellbagNumber = shellbagNumber,
                BagPath = parentKey.KeyPath + "\\" + valueName,
                LastWriteTime = ValidateTimestamp(parentKey.LastWriteTime?.UtcDateTime),
                IsValid = false,
                ParentPath = parentPath,
                FolderIsDeleted = false
            };

            try
            {
                if (rawBytes == null || rawBytes.Length < 2)
                {
                    return metadata;
                }

                string folderName = null;
                bool isDriveLetter = false;
                bool isRootGuid = false;

                if (rawBytes.Length > 2)
                {
                    byte shellItemType = rawBytes[2];

                    if (shellItemType == 0x1F && rawBytes.Length >= 20)
                    {
                        // Use PathCleaner's GuidPathResolver for GUID extraction
                        string guid = GuidPathResolver.ExtractGuidFromShellItem(rawBytes.Skip(4).Take(16).ToArray());
                        if (!string.IsNullOrEmpty(guid))
                        {
                            string guidName = GuidPathResolver.GetFolderNameFromGuid(guid);
                            isRootGuid = true;
                            folderName = guidName;
                            metadata.IsValid = true;
                        }
                    }
                    else if (shellItemType == 0x2F)
                    {
                        byte driveLetterByte = 0x00;
                        bool foundDriveLetter = false;

                        if (rawBytes.Length > 3 && rawBytes[3] >= 0x43 && rawBytes[3] <= 0x5A)
                        {
                            driveLetterByte = rawBytes[3];
                            foundDriveLetter = true;
                        }
                        else
                        {
                            for (int i = 3; i < Math.Min(rawBytes.Length, 27); i++)
                            {
                                if (rawBytes[i] >= 0x43 && rawBytes[i] <= 0x5A)
                                {
                                    driveLetterByte = rawBytes[i];
                                    foundDriveLetter = true;
                                    break;
                                }
                            }
                        }

                        if (foundDriveLetter)
                        {
                            char driveLetter = (char)driveLetterByte;
                            folderName = $"{char.ToUpper(driveLetter)}:\\";
                            isDriveLetter = true;
                            metadata.IsValid = true;
                        }
                    }
                }

                if (!isDriveLetter && !isRootGuid)
                {
                    int slot = int.TryParse(valueName, out int pos) ? pos : 0;
                    IShellBag shellBag = CreateShellBagFromBytes(rawBytes, parentKey.KeyPath, slot);

                    if (shellBag != null)
                    {
                        metadata.IsValid = true;
                        
                        try
                        {
                            metadata.RegIsDeleted = shellBag.IsDeleted;
                        }
                        catch
                        {
                            metadata.RegIsDeleted = false;
                        }

                        if (shellBag is ShellBag0X31 sb31)
                        {
                            try
                            {
                                // FIXED: Validate timestamp before assignment
                                metadata.LastModificationTime = ValidateTimestamp(sb31.LastModificationTime?.UtcDateTime);
                            }
                            catch (System.Security.Cryptography.CryptographicException)
                            {
                                // Ignore crypto exceptions when accessing timestamps
                            }
                            catch
                            {
                                // Ignore any other exceptions
                            }

                            try
                            {
                                // Safely check extension blocks count (can throw during lazy evaluation)
                                var blockCount = 0;
                                try
                                {
                                    blockCount = shellBag.ExtensionBlocks?.Count ?? 0;
                                }
                                catch (System.Security.Cryptography.CryptographicException)
                                {
                                    blockCount = 0;
                                }
                                catch
                                {
                                    blockCount = 0;
                                }

                                if (blockCount > 0)
                                {
                                    try
                                    {
                                        // Use OfType<> to safely filter before iteration
                                        var beefBlocks = shellBag.ExtensionBlocks.OfType<Beef0004>().ToList();
                                        foreach (var beef in beefBlocks)
                                        {
                                            try
                                            {
                                                // FIXED: Validate timestamp before assignment
                                                metadata.CreatedOnTime = ValidateTimestamp(beef.CreatedOnTime?.UtcDateTime);

                                                if (!string.IsNullOrEmpty(beef.LongName))
                                                {
                                                    folderName = beef.LongName;
                                                }

                                                if (beef.MFTInformation != null)
                                                {
                                                    metadata.MFTEntryNumber = beef.MFTInformation.MFTEntryNumber;
                                                    metadata.MFTSequenceNumber = beef.MFTInformation.MFTSequenceNumber;
                                                }
                                            }
                                            catch (System.Security.Cryptography.CryptographicException)
                                            {
                                                // Ignore crypto exceptions when accessing beef properties
                                            }
                                            catch
                                            {
                                                // Ignore any other exceptions
                                            }
                                        }
                                    }
                                    catch (System.Security.Cryptography.CryptographicException)
                                    {
                                        // Ignore crypto exceptions during OfType/ToList operation
                                    }
                                    catch
                                    {
                                        // Ignore any other exceptions during enumeration
                                    }
                                }
                            }
                            catch (System.Security.Cryptography.CryptographicException)
                            {
                                // Ignore crypto exceptions when enumerating extension blocks
                            }
                            catch
                            {
                                // Ignore any other exceptions
                            }

                            try
                            {
                                if (string.IsNullOrEmpty(folderName) && !string.IsNullOrEmpty(sb31.ShortName))
                                {
                                    folderName = sb31.ShortName;
                                }
                            }
                            catch (System.Security.Cryptography.CryptographicException)
                            {
                                // Ignore crypto exceptions when accessing ShortName
                            }
                            catch
                            {
                                // Ignore any other exceptions
                            }
                        }

                        try
                        {
                            if (string.IsNullOrEmpty(folderName) && shellBag != null && !string.IsNullOrEmpty(shellBag.Value))
                            {
                                folderName = shellBag.Value;
                            }
                        }
                        catch (System.Security.Cryptography.CryptographicException)
                        {
                            // Ignore crypto exceptions when accessing Value
                        }
                        catch
                        {
                            // Ignore any other exceptions
                        }
                    }
                }

                if (string.IsNullOrEmpty(folderName))
                {
                    folderName = "MISSING";
                }

                metadata.OutputPath = BuildCorrectFullPath(parentPath, folderName, isDriveLetter, isRootGuid);
                metadata.AbsolutePath = metadata.OutputPath;

                // NOTE: Path normalization removed here - will be done centrally in EvidenceAggregator
                // metadata.OutputPath = PathCleaner.NormalizePath(metadata.OutputPath);

                // FIXED: Shellbags are historical traces - don't check folder existence
                // Always set FolderIsDeleted to false (unknown status)
                metadata.FolderIsDeleted = false;
            }
            catch
            {
            }

            return metadata;
        }

        private static string BuildCorrectFullPath(string parentPath, string currentName, bool isDriveLetter, bool isRootGuid)
        {
            if (isDriveLetter)
            {
                return currentName;
            }

            if (isRootGuid)
            {
                return currentName;
            }

            if (string.IsNullOrEmpty(parentPath))
            {
                return currentName;
            }

            if (currentName != null && currentName.Length >= 2 && currentName[1] == ':')
            {
                return currentName;
            }

            if (parentPath.Length >= 2 && parentPath[1] == ':')
            {
                if (parentPath.EndsWith("\\"))
                {
                    return $"{parentPath}{currentName}";
                }
                else
                {
                    return $"{parentPath}\\{currentName}";
                }
            }

            return $"{parentPath}\\{currentName}";
        }

        // Only creates for appropriate shell item types
        private static IShellBag CreateShellBagFromBytes(byte[] rawBytes, string bagPath, int slot)
        {
            if (rawBytes == null || rawBytes.Length < 3)
            {
                return null;
            }

            byte shellItemType = rawBytes[2];
            
            // Skip problematic shell item types that cause CryptographicException
            // 0x00 = Unknown/Invalid
            // 0x1F = Root folder (handled separately above)
            // 0x2F = Drive letter (handled separately above)
            // 0x40-0x4F = File entry with special handling (can cause issues)
            // 0x61-0x6F = URI/MTP/FTP items (causes crypto exceptions)
            // 0x71 = Control Panel items (causes crypto exceptions)
            // 0x74 = Network locations (can cause crypto exceptions)
            // 0xB1 = FTP folder items (causes crypto exceptions)
            // 0xC3 = Network printer (causes crypto exceptions)
            if (shellItemType == 0x00 || shellItemType == 0x1F || shellItemType == 0x2F ||
                (shellItemType >= 0x40 && shellItemType <= 0x4F) ||
                (shellItemType >= 0x61 && shellItemType <= 0x6F) ||
                shellItemType == 0x71 || shellItemType == 0x74 ||
                shellItemType == 0xB1 || shellItemType == 0xC3)
            {
                return null;
            }
            
            // Additional validation: check for minimum viable size
            // Shell bags need at least 8 bytes for basic structure
            if (rawBytes.Length < 8)
            {
                return null;
            }
            
            // Only process file system entries (0x30-0x36 range)
            // These are the most reliable types for timeline analysis
            if (shellItemType < 0x30 || shellItemType > 0x36)
            {
                return null;
            }

            try
            {
                var shellbag = new ShellBag0X31(slot, slot, rawBytes, bagPath);
                return shellbag;
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                // Silently ignore cryptographic exceptions from malformed extension blocks
                // These are thrown by the ExtensionBlocks library when parsing corrupted data
                return null;
            }
            catch (Exception)
            {
                // Catch any other parsing exceptions
                return null;
            }
        }
    }
}