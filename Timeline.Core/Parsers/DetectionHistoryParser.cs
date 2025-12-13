using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Timeline.Core.Models;
using Timeline.Core.Util;
using System.Collections.Concurrent;
using System.Threading.Tasks;

// Parses Windows Defender detection history files
namespace Timeline.Core.Parsers
{
    // Extracts threat detection records from binary files
    public static class DetectionHistoryParser
    {
        private const string DETECTION_HISTORY_PATH = @"ProgramData\Microsoft\Windows Defender\Scans\History\Service\DetectionHistory";

        /// <summary>
        /// Parse Windows Defender Detection History files
        /// </summary>
        public static List<RegistryEntry> ParseDetectionHistory(Action<string> logger = null)
        {
            var timelineEntries = new List<RegistryEntry>();
            
            try
            {
                string detectionHistoryPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    @"Microsoft\Windows Defender\Scans\History\Service\DetectionHistory");
                
                if (!Directory.Exists(detectionHistoryPath))
                {
                    return timelineEntries;
                }

                var threatDirs = Directory.GetDirectories(detectionHistoryPath);

                var bag = new ConcurrentBag<RegistryEntry>();

                Parallel.ForEach(threatDirs, threatDir =>
                {
                    try
                    {
                        var detectionFiles = Directory.GetFiles(threatDir);
                        
                        foreach (var detectionFile in detectionFiles)
                        {
                            try
                            {
                                FileInfo fi = new FileInfo(detectionFile);
                                byte[] fileBytes = File.ReadAllBytes(detectionFile);
                                
                                if (fileBytes.Length < 100)
                                    continue;

                                string detectionPath = ExtractFilePath(fileBytes);
                                string threatName = ExtractThreatName(fileBytes);

                                var timelineEntry = new RegistryEntry
                                {
                                    Timestamp = new DateTimeOffset(fi.CreationTime),
                                    Path = StringPool.InternPath(detectionPath),
                                    Description = StringPool.InternDescription("Threat"),
                                    Source = StringPool.InternSource("DetectHistory"),
                                    OtherInfo = StringPool.InternOtherInfo(threatName)
                                };

                                bag.Add(timelineEntry);
                            }
                            catch
                            {
                            }
                        }
                    }
                    catch
                    {
                    }
                });

                timelineEntries.AddRange(bag);
            }
            catch
            {
            }

            return timelineEntries;
        }

        /// <summary>
        /// Extract file path - returns the first valid path found (the actual detection target)
        /// </summary>
        private static string ExtractFilePath(byte[] data)
        {
            // Look for drive letter pattern (C:\, D:\, etc.) in Unicode
            for (int i = 0; i < data.Length - 10; i++)
            {
                if (data[i] >= 'A' && data[i] <= 'Z' && 
                    data[i + 1] == 0 &&
                    data[i + 2] == ':' &&
                    data[i + 3] == 0 &&
                    data[i + 4] == '\\' &&
                    data[i + 5] == 0)
                {
                    string path = ExtractUnicodeString(data, i, 260);
                    path = CleanPath(path);
                    
                    if (IsValidPath(path))
                    {
                        return path;
                    }
                }
            }

            return "Unknown Path";
        }

        /// <summary>
        /// Extract threat name from the file
        /// </summary>
        private static string ExtractThreatName(byte[] data)
        {
            // Look for common threat prefixes
            string[] threatPrefixes = new[] { "Trojan:", "Virus:", "Worm:", "Backdoor:", "Trojan", "Virus", "Worm", "Backdoor", "Ransom", "HackTool", "Tool:", "PUA:", "Adware" };
            
            foreach (var prefix in threatPrefixes)
            {
                byte[] prefixBytes = Encoding.Unicode.GetBytes(prefix);
                int index = FindBytes(data, prefixBytes);
                if (index >= 0)
                {
                    string name = ExtractUnicodeString(data, index, 150);
                    if (IsValidThreatName(name))
                    {
                        return name;
                    }
                }
            }

            // Scan common offset ranges for strings
            int[] commonOffsets = new[] { 0x20, 0x30, 0x40, 0x50, 0x60, 0x80, 0x100, 0x200 };
            foreach (var offset in commonOffsets)
            {
                if (offset < data.Length - 20)
                {
                    string name = ExtractUnicodeString(data, offset, 150);
                    if (IsValidThreatName(name) && name.Length > 5)
                    {
                        return name;
                    }
                }
            }

            return "Unknown Threat";
        }

        /// <summary>
        /// Validate if a string looks like a valid threat name
        /// </summary>
        private static bool IsValidThreatName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length < 3)
                return false;

            int printableCount = 0;
            int totalCount = 0;

            foreach (char c in name)
            {
                totalCount++;
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || 
                    (c >= '0' && c <= '9') || c == ':' || c == '.' || c == '_' || 
                    c == '-' || c == '!' || c == '/' || c == ' ')
                {
                    printableCount++;
                }
            }

            return totalCount > 0 && (printableCount * 100 / totalCount) >= 70;
        }

        /// <summary>
        /// Clean up extracted path string
        /// </summary>
        private static string CleanPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            int nullIndex = path.IndexOf('\0');
            if (nullIndex > 0)
            {
                path = path.Substring(0, nullIndex);
            }

            var sb = new StringBuilder();
            foreach (char c in path)
            {
                if (!char.IsControl(c) || c == '\r' || c == '\n')
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Validate if a string looks like a valid file path
        /// </summary>
        private static bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 6)
                return false;

            if (!((path[0] >= 'A' && path[0] <= 'Z') && path[1] == ':' && path[2] == '\\'))
                return false;

            if (!path.Substring(3).Contains("\\"))
                return false;

            foreach (char c in path)
            {
                if (char.IsControl(c) && c != '\r' && c != '\n')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Extract Unicode string from byte array
        /// </summary>
        private static string ExtractUnicodeString(byte[] data, int startOffset, int maxLength)
        {
            var sb = new StringBuilder();
            
            for (int i = startOffset; i < Math.Min(data.Length - 1, startOffset + maxLength * 2); i += 2)
            {
                if (i + 1 >= data.Length)
                    break;

                char c = BitConverter.ToChar(data, i);
                
                if (c == '\0')
                    break;
                
                if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t')
                    continue;
                
                sb.Append(c);
            }
            
            string result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? "Unknown" : result;
        }

        /// <summary>
        /// Find byte pattern in array
        /// </summary>
        private static int FindBytes(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                if (FindBytesAt(haystack, needle, i))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Check if byte pattern exists at specific position
        /// </summary>
        private static bool FindBytesAt(byte[] haystack, byte[] needle, int position)
        {
            if (position + needle.Length > haystack.Length)
                return false;

            for (int i = 0; i < needle.Length; i++)
            {
                if (haystack[position + i] != needle[i])
                    return false;
            }
            return true;
        }
    }
}
