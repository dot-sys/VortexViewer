using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

// Path normalization and interning utilities
namespace Timeline.Core.Util
{
    // Cleans and normalizes file paths
    public static class PathCleaner
    {
        // Use concurrent dictionaries to reduce locking and allow parallel processing
        private static readonly ConcurrentDictionary<string, string> _pathInternPool = new ConcurrentDictionary<string, string>(Environment.ProcessorCount, 10000);
        private static readonly ConcurrentDictionary<string, string> _descriptionInternPool = new ConcurrentDictionary<string, string>(Environment.ProcessorCount, 1024);
        private static readonly ConcurrentDictionary<string, string> _sourceInternPool = new ConcurrentDictionary<string, string>(Environment.ProcessorCount, 1024);
        private static readonly ConcurrentDictionary<string, string> _otherInfoInternPool = new ConcurrentDictionary<string, string>(Environment.ProcessorCount, 4096);

        public static void CleanAllPaths(List<Models.RegistryEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            var uniquePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Path))
                    uniquePaths.Add(entry.Path);
            }
            var pathMapping = new ConcurrentDictionary<string, string>(Environment.ProcessorCount, uniquePaths.Count);

            System.Threading.Tasks.Parallel.ForEach(uniquePaths, originalPath =>
            {
                try
                {
                    var cleanedPath = NormalizePath(originalPath);
                    cleanedPath = InternString(cleanedPath, _pathInternPool);

                    if (!string.Equals(cleanedPath, originalPath, StringComparison.Ordinal))
                    {
                        pathMapping[originalPath] = cleanedPath;
                    }
                }
                catch { }
            });

            System.Threading.Tasks.Parallel.For(0, entries.Count, i =>
            {
                var entry = entries[i];
                if (entry == null) return;

                try
                {
                    if (!string.IsNullOrEmpty(entry.Path))
                    {
                        if (pathMapping.TryGetValue(entry.Path, out var cleanedPath))
                        {
                            entry.Path = cleanedPath;
                        }
                        else
                        {
                            entry.Path = InternString(entry.Path, _pathInternPool);
                        }
                    }

                    if (!string.IsNullOrEmpty(entry.Description))
                        entry.Description = InternString(entry.Description, _descriptionInternPool);

                    if (!string.IsNullOrEmpty(entry.Source))
                        entry.Source = InternString(entry.Source, _sourceInternPool);

                    if (!string.IsNullOrEmpty(entry.OtherInfo))
                        entry.OtherInfo = InternString(entry.OtherInfo, _otherInfoInternPool);
                }
                catch { }
            });
        }

        private static string InternString(string value, ConcurrentDictionary<string, string> pool)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return pool.GetOrAdd(value, value);
        }

        public static void UppercaseAllStrings(List<Models.RegistryEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            System.Threading.Tasks.Parallel.ForEach(entries, entry =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(entry.Path))
                        entry.Path = InternString(entry.Path.ToUpperInvariant(), _pathInternPool);

                    if (!string.IsNullOrEmpty(entry.Description))
                        entry.Description = InternString(entry.Description.ToUpperInvariant(), _descriptionInternPool);

                    if (!string.IsNullOrEmpty(entry.OtherInfo))
                        entry.OtherInfo = InternString(entry.OtherInfo.ToUpperInvariant(), _otherInfoInternPool);

                    if (!string.IsNullOrEmpty(entry.Source))
                        entry.Source = InternString(entry.Source.ToUpperInvariant(), _sourceInternPool);
                }
                catch { }
            });
        }

        public static void TitleCaseAllStrings(List<Models.RegistryEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            System.Threading.Tasks.Parallel.ForEach(entries, entry =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(entry.Path))
                        entry.Path = InternString(ApplyTitleCase(entry.Path), _pathInternPool);

                    if (!string.IsNullOrEmpty(entry.Description))
                        entry.Description = InternString(ApplyTitleCase(entry.Description), _descriptionInternPool);

                    if (!string.IsNullOrEmpty(entry.OtherInfo))
                        entry.OtherInfo = InternString(ApplyTitleCase(entry.OtherInfo), _otherInfoInternPool);

                    if (!string.IsNullOrEmpty(entry.Source))
                        entry.Source = InternString(ApplyTitleCase(entry.Source), _sourceInternPool);
                }
                catch { }
            });
        }

        private static string ApplyTitleCase(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var chars = input.ToCharArray();
            bool capitalizeNext = true;
            int lastDotIndex = input.LastIndexOf('.');

            for (int i = 0; i < chars.Length; i++)
            {
                if (lastDotIndex >= 0 && i > lastDotIndex)
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }
                else if (capitalizeNext && char.IsLetter(chars[i]))
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                    capitalizeNext = false;
                }
                else if (chars[i] == ' ' || chars[i] == '\\')
                {
                    capitalizeNext = true;
                }
                else
                {
                    chars[i] = char.ToLowerInvariant(chars[i]);
                }
            }

            return new string(chars);
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            path = WindowsPathExtractor.ExtractPath(path);
            path = CorruptedPathCleaner.CleanPath(path);
            path = EnvironmentVariableResolver.ExpandEnvironmentVariables(path);
            path = UwpAppResolver.ResolveUwpAppPath(path);
            path = GuidPathResolver.ResolveGuidPath(path);
            path = VolumeGuidResolver.ResolveVolumeGuid(path);
            path = SpecialFolderResolver.ResolveSpecialFolder(path);
            path = ShortPathExpander.ExpandShortPath(path);

            if (path.Length >= 2 && path[1] == ':' && char.IsLetter(path[0]))
            {
                path = char.ToUpperInvariant(path[0]) + path.Substring(1);
            }

            return path;
        }
    }

    // Expands environment variable placeholders
    public static class EnvironmentVariableResolver
    {
        private static readonly Dictionary<string, string> _commonVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "%programfiles%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
            { "%programfiles(x86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) },
            { "%programdata%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) },
            { "%userprofile%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
            { "%appdata%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
            { "%localappdata%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
            { "%temp%", Path.GetTempPath().TrimEnd('\\') },
            { "%tmp%", Path.GetTempPath().TrimEnd('\\') },
            { "%windir%", Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
            { "%systemroot%", Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
            { "%systemdrive%", Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) },
            { "%public%", Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments).Replace("\\Documents", "") },
            { "%allusersprofile%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) },
            { "%homedrive%", Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) },
            { "%homepath%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).Replace(Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), "") }
        };

        private static readonly Dictionary<string, string> _abbreviatedForms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "[ProgramFilesX64]", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
            { "[ProgramFilesX86]", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) },
            { "[ProgramFiles]", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) },
            { "[Windows]", Environment.GetFolderPath(Environment.SpecialFolder.Windows) },
            { "[System]", Environment.GetFolderPath(Environment.SpecialFolder.System) },
            { "[SystemX86]", Environment.GetFolderPath(Environment.SpecialFolder.SystemX86) },
            { "[ProgramData]", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) },
            { "[UserProfile]", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) },
            { "[AppData]", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) },
            { "[LocalAppData]", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) },
            { "[CommonFiles]", Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles) },
            { "[CommonFilesX86]", Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86) }
        };

        public static string ExpandEnvironmentVariables(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string result = path;

            if (result.Contains("["))
            {
                foreach (var kvp in _abbreviatedForms)
                {
                    if (result.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int index = 0;
                        while ((index = result.IndexOf(kvp.Key, index, StringComparison.OrdinalIgnoreCase)) >= 0)
                        {
                            result = result.Remove(index, kvp.Key.Length).Insert(index, kvp.Value);
                            index += kvp.Value.Length;
                        }
                    }
                }
            }

            if (!result.Contains("%"))
                return result;

            foreach (var kvp in _commonVariables)
            {
                if (result.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int index = 0;
                    while ((index = result.IndexOf(kvp.Key, index, StringComparison.OrdinalIgnoreCase)) >= 0)
                    {
                        result = result.Remove(index, kvp.Key.Length).Insert(index, kvp.Value);
                        index += kvp.Value.Length;
                    }
                }
            }

            if (result.Contains("%"))
            {
                try
                {
                    result = Environment.ExpandEnvironmentVariables(result);
                }
                catch
                {
                }
            }

            return result;
        }
    }

    // Resolves UWP app package paths
    public static class UwpAppResolver
    {
        private static readonly Dictionary<string, string> _knownUwpApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft.WindowsNotepad", "Notepad (UWP)" },
            { "Microsoft.WindowsCalculator", "Calculator (UWP)" },
            { "Microsoft.WindowsStore", "Microsoft Store" },
            { "Microsoft.Windows.Photos", "Photos (UWP)" },
            { "Microsoft.WindowsMaps", "Maps (UWP)" },
            { "Microsoft.WindowsCamera", "Camera (UWP)" },
            { "Microsoft.WindowsAlarms", "Alarms & Clock (UWP)" },
            { "Microsoft.MicrosoftEdge", "Microsoft Edge (Legacy)" },
            { "Microsoft.WindowsSoundRecorder", "Sound Recorder (UWP)" },
            { "Microsoft.Office.OneNote", "OneNote (UWP)" },
            { "Microsoft.MicrosoftStickyNotes", "Sticky Notes (UWP)" },
            { "Microsoft.GetHelp", "Get Help (UWP)" },
            { "Microsoft.WindowsFeedbackHub", "Feedback Hub (UWP)" },
            { "Microsoft.Paint", "Paint (UWP)" },
            { "SecHealthUI", "Windows Security (UWP)" },
            { "ShellExtension", "Shell Extension (UWP)" }
        };

        private static Dictionary<string, string> _uwpAppPaths = null;
        private static readonly object _uwpCacheLock = new object();

        public static string ResolveUwpAppPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Case 1: Already marked as "(UWP App)" - try to find installation path
            if (path.EndsWith("(UWP App)", StringComparison.OrdinalIgnoreCase))
            {
                var appName = path.Replace("(UWP App)", "").Trim();
                var installPath = TryFindUwpAppPath(appName);
                if (!string.IsNullOrEmpty(installPath))
                    return installPath;
                
                // If not found, return friendly name if we know it
                if (_knownUwpApps.TryGetValue(appName, out var friendlyName))
                    return friendlyName;
                
                return path; // Keep original format
            }

            // Case 2: Check if this is a UWP app package name format (contains underscore and exclamation mark)
            if (path.Contains("_") && path.Contains("!"))
            {
                // Extract the package name (before the underscore)
                int underscoreIndex = path.IndexOf('_');
                if (underscoreIndex > 0)
                {
                    string packageName = path.Substring(0, underscoreIndex);

                    // Try to find the installation path
                    var installPath = TryFindUwpAppPath(packageName);
                    if (!string.IsNullOrEmpty(installPath))
                        return installPath;

                    // Check if we have a known mapping
                    if (_knownUwpApps.TryGetValue(packageName, out var friendlyName))
                        return friendlyName;

                    // If not in known list, return a formatted version
                    // Extract just the last part of the package name (e.g., "WindowsNotepad" from "Microsoft.WindowsNotepad")
                    string[] parts = packageName.Split('.');
                    string appName = parts.Length > 1 ? parts[parts.Length - 1] : packageName;
                    
                    return $"{appName} (UWP App)";
                }
            }

            // Case 3: Just a plain app name - check if it's a known UWP app
            if (_knownUwpApps.TryGetValue(path, out var knownFriendlyName))
            {
                var installPath = TryFindUwpAppPath(path);
                if (!string.IsNullOrEmpty(installPath))
                    return installPath;
                return knownFriendlyName;
            }

            return path;
        }

        /// <summary>
        /// Try to find the actual installation path for a UWP app
        /// </summary>
        private static string TryFindUwpAppPath(string appName)
        {
            try
            {
                // Build cache of UWP app paths on first use
                lock (_uwpCacheLock)
                {
                    if (_uwpAppPaths == null)
                    {
                        _uwpAppPaths = BuildUwpAppPathCache();
                    }
                }

                // Try exact match first
                if (_uwpAppPaths.TryGetValue(appName, out var exactPath))
                    return exactPath;

                // Try partial match (case-insensitive contains)
                var partialMatch = _uwpAppPaths.FirstOrDefault(kvp => 
                    kvp.Key.IndexOf(appName, StringComparison.OrdinalIgnoreCase) >= 0);
                
                if (!string.IsNullOrEmpty(partialMatch.Value))
                    return partialMatch.Value;
            }
            catch
            {
                // Ignore errors during path resolution
            }

            return null;
        }

        /// <summary>
        /// Build a cache of UWP app package names to their installation paths
        /// </summary>
        private static Dictionary<string, string> BuildUwpAppPathCache()
        {
            var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // UWP apps are typically installed in C:\Program Files\WindowsApps
                var windowsAppsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
                
                if (Directory.Exists(windowsAppsPath))
                {
                    var directories = Directory.GetDirectories(windowsAppsPath);
                    
                    foreach (var dir in directories)
                    {
                        var dirName = Path.GetFileName(dir);
                        
                        // Extract package name (before first underscore)
                        var underscoreIndex = dirName.IndexOf('_');
                        if (underscoreIndex > 0)
                        {
                            var packageName = dirName.Substring(0, underscoreIndex);
                            
                            // Try to find the main executable
                            var exePath = FindUwpExecutable(dir, packageName);
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                cache[packageName] = exePath;
                            }
                            else
                            {
                                // No executable found, just use the package folder
                                cache[packageName] = dir;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore errors - we'll return what we can
            }

            return cache;
        }

        /// <summary>
        /// Try to find the main executable for a UWP app package
        /// </summary>
        private static string FindUwpExecutable(string packagePath, string packageName)
        {
            try
            {
                // Common patterns for UWP executables
                var possibleExeNames = new[]
                {
                    $"{packageName}.exe",
                    $"{Path.GetFileName(packagePath)}.exe",
                    "App.exe",
                    "Application.exe"
                };

                // Search for executables in the package directory
                var exeFiles = Directory.GetFiles(packagePath, "*.exe", SearchOption.TopDirectoryOnly);
                
                // Try to find a match with our expected names
                foreach (var expectedName in possibleExeNames)
                {
                    var match = exeFiles.FirstOrDefault(f => 
                        Path.GetFileName(f).Equals(expectedName, StringComparison.OrdinalIgnoreCase));
                    
                    if (!string.IsNullOrEmpty(match))
                        return match;
                }

                // If no match, return the first .exe we found
                if (exeFiles.Length > 0)
                    return exeFiles[0];
            }
            catch
            {
                // Ignore errors
            }

            return null;
        }
    }

    // Resolves GUID paths to descriptions
    public static class GuidPathResolver
    {
        public static string ResolveGuidPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Check if path starts with a GUID pattern {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
            if (!path.StartsWith("{") || path.Length < 38)
                return path;

            int closingBrace = path.IndexOf('}');
            if (closingBrace <= 0)
                return path;

            // Extract the GUID
            string guidString = path.Substring(0, closingBrace + 1);
            
            try
            {
                // Try to resolve using GuidMapping library
                var description = GuidMapping.GuidMapping.GetDescriptionFromGuid(guidString);
                if (!string.IsNullOrEmpty(description))
                {
                    // Get the rest of the path after the GUID
                    string remainder = closingBrace + 1 < path.Length ? path.Substring(closingBrace + 1) : "";
                    
                    // Clean up the remainder (remove leading backslash if present)
                    if (remainder.StartsWith("\\"))
                        remainder = remainder.Substring(1);
                    
                    // Return formatted path
                    if (string.IsNullOrEmpty(remainder))
                        return $"[{description}]";
                    else
                        return $"[{description}]\\{remainder}";
                }
            }
            catch
            {
                // GuidMapping failed, continue with original path
            }

            return path;
        }

        /// <summary>
        /// Extract a GUID from a 16-byte shell item array (helper method for other parsers)
        /// </summary>
        public static string ExtractGuidFromShellItem(byte[] guidBytes)
        {
            if (guidBytes == null || guidBytes.Length != 16)
                return null;

            try
            {
                var guid = new Guid(guidBytes);
                return guid.ToString().ToUpperInvariant();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Get a human-readable folder name from a GUID (helper method for other parsers)
        /// </summary>
        public static string GetFolderNameFromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid))
                return null;

            try
            {
                // Try GuidMapping library first
                var description = GuidMapping.GuidMapping.GetDescriptionFromGuid(guid);
                if (!string.IsNullOrEmpty(description))
                {
                    return description;
                }
            }
            catch
            {
                // GuidMapping failed
            }

            // Return formatted GUID if no mapping found
            return $"GUID:{guid}";
        }
    }

    // Resolves volume GUIDs to drive letters
    public static class VolumeGuidResolver
    {
        public static string ResolveVolumeGuid(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Pattern: "Unmapped GUID: {guid}\path"
            const string unmappedPrefix = "Unmapped GUID: ";
            if (path.StartsWith(unmappedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var afterPrefix = path.Substring(unmappedPrefix.Length);
                var backslashIndex = afterPrefix.IndexOf('\\');
                
                if (backslashIndex > 0)
                {
                    var volumeIdentifier = afterPrefix.Substring(0, backslashIndex);
                    var remainingPath = afterPrefix.Substring(backslashIndex + 1);

                    // Try volume serial number resolution (for shellbags, these are often serial numbers)
                    var driveLetter = VolumeSerialNumberMapper.GetDriveLetterFromVolumeSerial(volumeIdentifier);
                    
                    if (!string.IsNullOrEmpty(driveLetter))
                    {
                        return $"{driveLetter}\\{remainingPath}";
                    }

                    // Try device path mapper as fallback
                    driveLetter = DevicePathMapper.GetDriveLetterFromPartialGuid(volumeIdentifier);
                    
                    if (!string.IsNullOrEmpty(driveLetter))
                    {
                        return $"{driveLetter}\\{remainingPath}";
                    }

                    // Try GuidMapping library
                    try
                    {
                        var description = GuidMapping.GuidMapping.GetDescriptionFromGuid(volumeIdentifier);
                        if (!string.IsNullOrEmpty(description))
                        {
                            return $"[{description}]\\{remainingPath}";
                        }
                    }
                    catch
                    {
                        // Ignore
                    }

                    // If still unmapped, return with clearer formatting
                    return $"[Volume: {volumeIdentifier}]\\{remainingPath}";
                }
                else if (backslashIndex == -1)
                {
                    // No path after GUID
                    var volumeIdentifier = afterPrefix;
                    var driveLetter = VolumeSerialNumberMapper.GetDriveLetterFromVolumeSerial(volumeIdentifier);
                    return driveLetter ?? $"[Volume: {volumeIdentifier}]";
                }
            }

            return path;
        }
    }

    // Resolves special folder placeholders
    public static class SpecialFolderResolver
    {
        public static string ResolveSpecialFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Handle "Shared Documents Folder (Users Files)" pattern
            if (path.Contains("Shared Documents Folder (Users Files)") || 
                path.StartsWith("Shared Documents Folder (Users Files)", StringComparison.OrdinalIgnoreCase))
            {
                var userDocsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                
                // Replace the placeholder with actual user documents path
                if (path.Equals("Shared Documents Folder (Users Files)", StringComparison.OrdinalIgnoreCase))
                {
                    return userDocsPath;
                }
                else
                {
                    // Path has additional components after the placeholder
                    var suffix = path.Substring("Shared Documents Folder (Users Files)".Length).TrimStart('\\');
                    return Path.Combine(userDocsPath, suffix);
                }
            }

            // Handle other known special folders
            if (path.StartsWith("ControlPanelHome", StringComparison.OrdinalIgnoreCase))
            {
                // Control Panel items - keep the prefix for clarity
                return path;
            }

            return path;
        }
    }

    // Expands 8.3 short paths
    public static class ShortPathExpander
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint GetLongPathName(
            [MarshalAs(UnmanagedType.LPTStr)] string lpszShortPath,
            [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszLongPath,
            uint cchBuffer);

        public static string ExpandShortPath(string shortPath)
        {
            if (string.IsNullOrEmpty(shortPath))
                return shortPath;

            // Check if path contains short name pattern (contains ~)
            if (!shortPath.Contains("~"))
                return shortPath;

            try
            {
                const uint bufferSize = 32768; // Max path length
                var longPathBuffer = new StringBuilder((int)bufferSize);
                uint result = GetLongPathName(shortPath, longPathBuffer, bufferSize);

                if (result > 0 && result <= bufferSize)
                {
                    return longPathBuffer.ToString();
                }
            }
            catch
            {
                // Fall through to return original
            }

            return shortPath;
        }
    }

    // Detects and cleans corrupted paths
    public static class CorruptedPathCleaner
    {
        public static string CleanPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            bool hasInvalidChars = false;
            foreach (char c in path)
            {
                if (char.IsControl(c) && c != '\r' && c != '\n')
                {
                    hasInvalidChars = true;
                    break;
                }
                
                if (c < 32 && c != '\t')
                {
                    hasInvalidChars = true;
                    break;
                }
            }

            if (hasInvalidChars)
            {
                var validPrefix = new StringBuilder();
                foreach (char c in path)
                {
                    if (char.IsControl(c) || c < 32)
                        break;
                    validPrefix.Append(c);
                }

                var cleaned = validPrefix.ToString().TrimEnd('\\');
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    return "[Corrupted Path]";
                }
                
                return cleaned + " [Corrupted]";
            }

            return path;
        }
    }

    // Extracts Windows paths from strings
    public static class WindowsPathExtractor
    {
        private static readonly Regex WindowsPathRegex = new Regex(
            @"[a-zA-Z]:\\(?:[^\\/:*?<>|""]+\\)*[^\\/:*?<>|""]+\.[a-zA-Z0-9]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase
        );

        public static string ExtractPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return input;

            string cleaned = input.Trim();
            
            if (cleaned.StartsWith("\"") || cleaned.StartsWith("'"))
            {
                cleaned = cleaned.Substring(1);
            }
            
            if (cleaned.EndsWith("\"") || cleaned.EndsWith("'"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 1);
            }

            var match = WindowsPathRegex.Match(cleaned);
            if (match.Success)
            {
                var extractedPath = match.Value.Trim();
                extractedPath = extractedPath.TrimEnd('"', '\'', ' ');
                return extractedPath;
            }

            return cleaned.Trim();
        }

        public static bool ContainsWindowsPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return WindowsPathRegex.IsMatch(input);
        }

        public static List<string> ExtractAllPaths(string input)
        {
            var paths = new List<string>();
            
            if (string.IsNullOrWhiteSpace(input))
                return paths;

            var matches = WindowsPathRegex.Matches(input);
            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    var path = match.Value.Trim('"', '\'', ' ');
                    paths.Add(path);
                }
            }

            return paths;
        }
    }
}
