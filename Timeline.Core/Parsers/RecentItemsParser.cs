using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Lnk;
using Timeline.Core.Models;

// Parses shortcut files from Recent folder
namespace Timeline.Core.Parsers
{
    // Extracts file access history from lnk files
    public static class RecentItemsParser
    {
        public static List<RegistryEntry> ParseRecentItems(Action<string> logAction = null)
        {
            var entries = new List<RegistryEntry>();
            
            try
            {
                var recentPaths = GetRecentFolderPaths();
                
                if (recentPaths.Count == 0)
                {
                    return entries;
                }

                var allParsedEntries = new List<RegistryEntry>();
                
                Parallel.ForEach(recentPaths, recentPath =>
                {
                    try
                    {
                        var folderEntries = ParseRecentFolder(recentPath);
                        lock (allParsedEntries)
                        {
                            allParsedEntries.AddRange(folderEntries);
                        }
                    }
                    catch (Exception)
                    {
                    }
                });

                entries = RemoveDuplicates(allParsedEntries);
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static List<string> GetRecentFolderPaths()
        {
            var paths = new List<string>();

            try
            {
                var modernPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Recent");
                
                if (Directory.Exists(modernPath))
                {
                    paths.Add(modernPath);
                }
            }
            catch { }

            try
            {
                var username = Environment.UserName;
                var legacyPath = $@"C:\Documents and Settings\{username}\Recent";
                
                if (Directory.Exists(legacyPath))
                {
                    paths.Add(legacyPath);
                }
            }
            catch { }

            return paths;
        }

        private static List<RegistryEntry> ParseRecentFolder(string folderPath)
        {
            var entries = new List<RegistryEntry>();

            if (!Directory.Exists(folderPath))
            {
                return entries;
            }

            try
            {
                var lnkFiles = Directory.GetFiles(folderPath, "*.lnk");

                var parsedEntries = new List<RegistryEntry>();
                
                Parallel.ForEach(lnkFiles, lnkFile =>
                {
                    try
                    {
                        var entry = ParseLnkFile(lnkFile);
                        if (entry != null)
                        {
                            lock (parsedEntries)
                            {
                                parsedEntries.Add(entry);
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                });

                entries.AddRange(parsedEntries);
            }
            catch (Exception)
            {
            }

            return entries;
        }

        private static RegistryEntry ParseLnkFile(string lnkFilePath)
        {
            try
            {
                var lnkFile = Lnk.Lnk.LoadFile(lnkFilePath);

                var targetPath = !string.IsNullOrEmpty(lnkFile.LocalPath) 
                    ? lnkFile.LocalPath 
                    : lnkFile.CommonPath;
                
                if (string.IsNullOrEmpty(targetPath))
                {
                    return null;
                }

                DateTimeOffset timestamp;
                if (lnkFile.SourceModified.HasValue)
                {
                    timestamp = lnkFile.SourceModified.Value;
                }
                else
                {
                    var fileInfo = new FileInfo(lnkFilePath);
                    timestamp = new DateTimeOffset(fileInfo.LastWriteTime);
                }

                string description;
                var extension = Path.GetExtension(targetPath);
                if (string.IsNullOrEmpty(extension))
                {
                    description = "Accessed Folder";
                }
                else
                {
                    description = "Opens a File";
                }

                return new RegistryEntry
                {
                    Timestamp = timestamp,
                    Source = "Recent",
                    Description = description,
                    Path = targetPath,
                    OtherInfo = ""
                };
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static List<RegistryEntry> RemoveDuplicates(List<RegistryEntry> entries)
        {
            var seen = new HashSet<string>();
            var uniqueEntries = new List<RegistryEntry>();

            foreach (var entry in entries)
            {
                var key = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss}|{entry.Path}";
                
                if (!seen.Contains(key))
                {
                    seen.Add(key);
                    uniqueEntries.Add(entry);
                }
            }

            return uniqueEntries;
        }
    }
}
