using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Registry;
using Registry.Abstractions;
using Timeline.Core.Models;
using Timeline.Core.Util;

// Registry timeline artifact extraction utilities
namespace Timeline.Core.Parsers
{
    // Registry parser configuration and metadata
    internal class RegistryParserDefinition
    {
        public string KeyPath { get; }
        public string SourceName { get; }
        public string Description { get; }
        public Func<RegistryKey, IProgress<int>, CancellationToken, Task<List<RegistryEntry>>> ParseFunctionAsync { get; }
        public Func<RegistryKey, List<RegistryEntry>> ParseFunction { get; }
        public HiveType HiveType { get; }

        public RegistryParserDefinition(string keyPath, string sourceName, string description, Func<RegistryKey, List<RegistryEntry>> parseFunction, HiveType hiveType)
        {
            KeyPath = keyPath;
            SourceName = sourceName;
            Description = description;
            ParseFunction = parseFunction;
            HiveType = hiveType;
        }

        public RegistryParserDefinition(string keyPath, string sourceName, string description, Func<RegistryKey, IProgress<int>, CancellationToken, Task<List<RegistryEntry>>> parseFunctionAsync, HiveType hiveType)
        {
            KeyPath = keyPath;
            SourceName = sourceName;
            Description = description;
            ParseFunctionAsync = parseFunctionAsync;
            HiveType = hiveType;
        }
    }

    // Extracts registry artifacts for timeline analysis
    public static class GenericParser
    {
        private static readonly List<RegistryParserDefinition> ParserDefinitions;
        private static readonly SemaphoreSlim ParserSemaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        private const int BatchSize = 50;
        private const int MaxValueDataSize = 1024 * 1024;

        static GenericParser()
        {
            ParserDefinitions = new List<RegistryParserDefinition>
            {
                new RegistryParserDefinition(@"Microsoft\Windows\CurrentVersion\Uninstall", "Registry", "Program Installed", ParseUninstallKeyAsync, HiveType.SOFTWARE),
                new RegistryParserDefinition(@"Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "Registry", "Program Installed", ParseUninstallKeyAsync, HiveType.SOFTWARE),
                new RegistryParserDefinition(@"Microsoft\Windows\CurrentVersion\Run", "Run", "AutoRun", ParseRunKeyAsync, HiveType.SOFTWARE),

                new RegistryParserDefinition(@"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist", "UserAssist", "Run Executable", ParseUserAssistAsync, HiveType.NTUSER),
                new RegistryParserDefinition(@"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "AutoRun", ParseRunKeyAsync, HiveType.NTUSER),
                new RegistryParserDefinition(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", "RecentDocs", "Opens a File", ParseRecentDocsAsync, HiveType.NTUSER),
                new RegistryParserDefinition(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU", "RunMRU", "Opens a File", ParseMruListAsync, HiveType.NTUSER),
                new RegistryParserDefinition(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths", "TypedPaths", "Opens a File", (key, progress, ct) => ParseValueAsPathAsync(key, progress, ct), HiveType.NTUSER),
                new RegistryParserDefinition(@"Software\WinRAR\ArcHistory", "WinRAR History", "Opens an Archive", ParseWinRARHistoryAsync, HiveType.NTUSER),
                new RegistryParserDefinition(@"Software\Microsoft\Windows\CurrentVersion\Search\JumpListData", "JumpListData", "Run Executable", ParseJumpListDataAsync, HiveType.NTUSER),
                new RegistryParserDefinition(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Persisted", "CompatAssist Persisted", "Run Executable", ParseCompatAssistAsync, HiveType.NTUSER),
                new RegistryParserDefinition(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store", "CompatAssist Store", "Run Executable", ParseCompatAssistAsync, HiveType.NTUSER),

                new RegistryParserDefinition(@"Local Settings\Software\Microsoft\Windows\Shell\MuiCache", "MuiCache", "Run Executable", ParseMuiCacheAsync, HiveType.USRCLASS),
            };
        }

        public static async Task<List<RegistryEntry>> ParseHiveAsync(RegistryHive hive, HiveType hiveType, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var entries = new List<RegistryEntry>(500);
            if (hive?.Root == null)
            {
                return entries;
            }

            var applicableParsers = ParserDefinitions.Where(p => p.HiveType == hiveType).ToList();
            
            var tasks = new List<Task<(string Source, string Description, List<RegistryEntry> Entries)>>();

            foreach (var parser in applicableParsers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                tasks.Add(ProcessSingleParserAsync(hive, parser, cancellationToken));

                if (tasks.Count >= Environment.ProcessorCount)
                {
                    var results = await Task.WhenAll(tasks);
                    foreach (var (Source, Description, Entries) in results)
                    {
                        foreach (var entry in Entries)
                        {
                            entry.Source = Source;
                            entry.Description = Description;
                        }
                        entries.AddRange(Entries);
                    }
                    tasks.Clear();
                }
            }

            if (tasks.Count > 0)
            {
                var results = await Task.WhenAll(tasks);
                foreach (var (Source, Description, Entries) in results)
                {
                    foreach (var entry in Entries)
                    {
                        entry.Source = Source;
                        entry.Description = Description;
                    }
                    entries.AddRange(Entries);
                }

            }

            progress?.Report(100);
            return entries;
        }

        private static async Task<(string Source, string Description, List<RegistryEntry> Entries)> ProcessSingleParserAsync(RegistryHive hive, RegistryParserDefinition parser, CancellationToken cancellationToken)
        {
            await ParserSemaphore.WaitAsync(cancellationToken);
            try
            {
                var targetKey = hive.GetKey(parser.KeyPath);
                
                if (targetKey != null)
                {
                    List<RegistryEntry> parsedEntries;

                    if (parser.ParseFunctionAsync != null)
                    {
                        parsedEntries = await parser.ParseFunctionAsync(targetKey, null, cancellationToken);
                    }
                    else
                    {
                        parsedEntries = await Task.Run(() => parser.ParseFunction(targetKey), cancellationToken);
                    }

                    return (parser.SourceName, parser.Description, parsedEntries);
                }
                
                return (parser.SourceName, parser.Description, new List<RegistryEntry>());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return (parser.SourceName, parser.Description, new List<RegistryEntry>());
            }
            finally
            {
                ParserSemaphore.Release();
            }
        }

        public static async Task<List<RegistryEntry>> ParseKeyAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var entries = new List<RegistryEntry>(100);
            if (key?.Values == null)
                return entries;

            var values = key.Values.ToList();
            if (values.Count == 0)
                return entries;

            return await Task.Run(() =>
            {
                for (int i = 0; i < values.Count; i += BatchSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var batch = values.Skip(i).Take(BatchSize);
                    foreach (var value in batch)
                    {
                        try
                        {
                            if (value.ValueDataRaw?.Length > MaxValueDataSize)
                                continue;

                            entries.Add(new RegistryEntry
                            {
                                Timestamp = key.LastWriteTime ?? DateTimeOffset.MinValue,
                                Path = value.ValueData ?? "",
                                OtherInfo = $"Value: {value.ValueName ?? ""}"
                            });
                        }
                        catch (Exception)
                        {
                        }
                    }

                    if (i % (BatchSize * 5) == 0)
                        progress?.Report(((i + BatchSize) * 100) / values.Count);
                }
                return entries;
            }, cancellationToken);
        }

        public static List<RegistryEntry> ParseHive(RegistryHive hive, HiveType hiveType)
        {
            return ParseHiveAsync(hive, hiveType, null, CancellationToken.None).GetAwaiter().GetResult();
        }

        private static async Task<List<RegistryEntry>> ParseUserAssistAsync(RegistryKey userAssistKey, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var entriesBag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var subKeys = userAssistKey.SubKeys?.ToList() ?? new List<RegistryKey>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };

                Parallel.ForEach(subKeys, po, (guidSubKey, state, index) =>
                {
                    try
                    {
                        var countSubKey = guidSubKey.SubKeys?.FirstOrDefault(sk => sk.KeyName == "Count");
                        if (countSubKey == null) return;

                        var values = countSubKey.Values?.ToList() ?? new List<KeyValue>();
                        for (int j = 0; j < values.Count; j += BatchSize)
                        {
                            po.CancellationToken.ThrowIfCancellationRequested();

                            var batch = values.Skip(j).Take(BatchSize);
                            foreach (var value in batch)
                            {
                                try
                                {
                                    var data = value.ValueDataRaw;
                                    if (data == null || data.Length < 72 || data.Length > MaxValueDataSize)
                                        continue;

                                    var decodedName = Rot13(value.ValueName);
                                    var executionCount = BitConverter.ToInt32(data, 4);
                                    
                                    DateTimeOffset lastExecutedTimestamp = DateTimeOffset.MinValue;
                                    try
                                    {
                                        long fileTime = BitConverter.ToInt64(data, 60);
                                        if (fileTime > 0 && fileTime <= 2650467743999999999)
                                        {
                                            var timestamp = DateTimeOffset.FromFileTime(fileTime);
                                            var now = DateTimeOffset.Now;
                                            if (timestamp >= new DateTimeOffset(1995, 1, 1, 0, 0, 0, TimeSpan.Zero) && timestamp <= now.AddYears(10))
                                            {
                                                lastExecutedTimestamp = timestamp;
                                            }
                                        }
                                    }
                                    catch { }

                                    var rawPath = decodedName.Replace(":*:", "\\");
                                    var normalizedPath = PathCleaner.NormalizePath(rawPath);

                                    entriesBag.Add(new RegistryEntry
                                    {
                                        Timestamp = lastExecutedTimestamp,
                                        Source = "UserAssist",
                                        Description = "Run Executable",
                                        Path = normalizedPath,
                                        OtherInfo = $"Execution Count: {executionCount}"
                                    });
                                }
                                catch { }
                            }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                });
            }, cancellationToken);
            
            return entriesBag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseRunKeyAsync(RegistryKey runKey, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var values = runKey.Values?.ToList() ?? new List<KeyValue>();
            
            var bag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(values, po, value =>
                {
                    try
                    {
                        if (value.ValueDataRaw?.Length > MaxValueDataSize)
                            return;

                        var cleanPath = WindowsPathExtractor.ExtractPath(value.ValueData ?? "");
                        var normalizedPath = PathCleaner.NormalizePath(cleanPath);

                        bag.Add(new RegistryEntry
                        {
                            Timestamp = DateTimeOffset.MinValue,
                            Source = "Run",
                            Description = "AutoRun",
                            Path = normalizedPath,
                            OtherInfo = $"Value Name: {value.ValueName ?? ""}"
                        });
                    }
                    catch (Exception)
                    {
                    }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return bag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseRecentDocsAsync(RegistryKey recentDocsKey, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var extensionSubKeys = recentDocsKey.SubKeys?.ToList() ?? new List<RegistryKey>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };

                Parallel.ForEach(extensionSubKeys, po, extensionKey =>
                {
                    try
                    {
                        var values = extensionKey.Values?.Where(v => v.ValueName != "MRUListEx").ToList() ?? new List<KeyValue>();

                        foreach (var value in values)
                        {
                            try
                            {
                                var data = value.ValueDataRaw;
                                if (data == null || data.Length == 0 || data.Length > MaxValueDataSize)
                                    continue;

                                var path = Encoding.Unicode.GetString(data).Split('\0')[0];
                                if (string.IsNullOrEmpty(path)) continue;

                                var normalizedPath = PathCleaner.NormalizePath(path);
                                var utcTime = extensionKey.LastWriteTime ?? DateTimeOffset.MinValue;
                                var localTime = utcTime.ToLocalTime();

                                string description = "Opens a File";
                                if (!string.IsNullOrEmpty(normalizedPath))
                                {
                                    var pathExtension = Path.GetExtension(normalizedPath);
                                    if (string.IsNullOrEmpty(pathExtension))
                                    {
                                        description = "Accessed Folder";
                                    }
                                }

                                bag.Add(new RegistryEntry
                                {
                                    Timestamp = localTime,
                                    Source = "RecentDocs",
                                    Description = description,
                                    Path = normalizedPath,
                                    OtherInfo = $"MRU Value: {value.ValueName ?? ""}"
                                });
                            }
                            catch { }
                        }
                    }
                    catch { }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return bag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseMruListAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var entriesBag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var mruList = key.Values?.FirstOrDefault(v => v.ValueName == "MRUList");
            if (mruList == null) return new List<RegistryEntry>();

            var mruChars = mruList.ValueData?.ToCharArray() ?? new char[0];
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(mruChars, po, c =>
                {
                    try
                    {
                        var value = key.Values?.FirstOrDefault(v => v.ValueName == c.ToString());
                        if (value != null && value.ValueDataRaw?.Length <= MaxValueDataSize)
                        {
                            var pathData = value.ValueData?.Split('\0');
                            if (pathData?.Length > 0 && !string.IsNullOrEmpty(pathData[0]))
                            {
                                var normalizedPath = PathCleaner.NormalizePath(pathData[0]);

                                entriesBag.Add(new RegistryEntry
                                {
                                    Timestamp = DateTimeOffset.MinValue,
                                    Source = "RunMRU",
                                    Description = "Opens a File",
                                    Path = normalizedPath,
                                    OtherInfo = $"MRU: {c}"
                                });
                            }
                        }
                    }
                    catch { }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return entriesBag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseJumpListDataAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var subKeys = key.SubKeys?.ToList() ?? new List<RegistryKey>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(subKeys, po, subKey =>
                {
                    try
                    {
                        var keyName = subKey.KeyName ?? "";
                        
                        // Extract just the executable name from the full key path
                        var executableName = keyName;
                        var lastBackslash = keyName.LastIndexOf('\\');
                        if (lastBackslash >= 0 && lastBackslash < keyName.Length - 1)
                        {
                            executableName = keyName.Substring(lastBackslash + 1);
                        }
                        
                        if (executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            var normalizedPath = PathCleaner.NormalizePath(executableName);
                            var utcTime = subKey.LastWriteTime ?? DateTimeOffset.MinValue;
                            var localTime = utcTime.ToLocalTime();

                            bag.Add(new RegistryEntry
                            {
                                Timestamp = localTime,
                                Source = "JumpListData",
                                Description = "Run Executable",
                                Path = normalizedPath,
                                OtherInfo = "Jump List"
                            });
                        }
                    }
                    catch { }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return bag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseCompatAssistAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var values = key.Values?.ToList() ?? new List<KeyValue>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(values, po, value =>
                {
                    try
                    {
                        if (value.ValueDataRaw?.Length > MaxValueDataSize)
                            return;

                        var valueName = value.ValueName ?? "";
                        if (string.IsNullOrWhiteSpace(valueName)) return;

                        var normalizedPath = PathCleaner.NormalizePath(valueName);
                        bag.Add(new RegistryEntry
                        {
                            Timestamp = DateTimeOffset.MinValue,
                            Source = "CompatAssist",
                            Description = "Run Executable",
                            Path = normalizedPath,
                            OtherInfo = ""
                        });
                    }
                    catch { }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return bag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseSubKeyAsPathAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var subKeys = key.SubKeys?.ToList() ?? new List<RegistryKey>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(subKeys, po, subKey =>
                {
                    try
                    {
                        var normalizedPath = PathCleaner.NormalizePath(subKey.KeyName ?? "");
                        var utcTime = subKey.LastWriteTime ?? DateTimeOffset.MinValue;
                        var localTime = utcTime.ToLocalTime();

                        bag.Add(new RegistryEntry
                        {
                            Timestamp = localTime,
                            Path = normalizedPath,
                            OtherInfo = "Subkey as path"
                        });
                    }
                    catch { }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return bag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseUninstallKeyAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var subKeys = key.SubKeys?.ToList() ?? new List<RegistryKey>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(subKeys, po, subKey =>
                {
                    try
                    {
                        var installDateValue = subKey.Values?.FirstOrDefault(v => v.ValueName.Equals("InstallDate", StringComparison.OrdinalIgnoreCase));
                        var installLocationValue = subKey.Values?.FirstOrDefault(v => v.ValueName.Equals("InstallLocation", StringComparison.OrdinalIgnoreCase));

                        if (installLocationValue != null && !string.IsNullOrWhiteSpace(installLocationValue.ValueData))
                        {
                            DateTimeOffset timestamp = DateTimeOffset.MinValue;

                            if (installDateValue != null && !string.IsNullOrWhiteSpace(installDateValue.ValueData))
                            {
                                string installDate = installDateValue.ValueData.Trim();
                                if (installDate.Length == 8 && DateTime.TryParseExact(installDate, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime parsedDate))
                                {
                                    timestamp = new DateTimeOffset(parsedDate, TimeZoneInfo.Local.GetUtcOffset(parsedDate));
                                }
                            }

                            if (timestamp == DateTimeOffset.MinValue)
                            {
                                var utcTime = subKey.LastWriteTime ?? DateTimeOffset.MinValue;
                                timestamp = utcTime.ToLocalTime();
                            }

                            var normalizedPath = PathCleaner.NormalizePath(installLocationValue.ValueData.Trim());

                            // Extract just the program name from the full key path
                            var keyName = subKey.KeyName ?? "";
                            var programName = keyName;
                            var lastBackslash = keyName.LastIndexOf('\\');
                            if (lastBackslash >= 0 && lastBackslash < keyName.Length - 1)
                            {
                                programName = keyName.Substring(lastBackslash + 1);
                            }

                            bag.Add(new RegistryEntry
                            {
                                Timestamp = timestamp,
                                Source = "Registry",
                                Description = "Program Installed",
                                Path = normalizedPath,
                                OtherInfo = $"Program: {programName}"
                            });
                        }
                    }
                    catch { }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return bag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseMuiCacheAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var values = key.Values?.ToList() ?? new List<KeyValue>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(values, po, value =>
                {
                    try
                    {
                        var valueName = value.ValueName ?? "";
                        if (string.IsNullOrWhiteSpace(valueName)) return;

                        if (valueName.EndsWith(".FriendlyAppName", StringComparison.OrdinalIgnoreCase))
                        {
                            valueName = valueName.Substring(0, valueName.Length - ".FriendlyAppName".Length);
                        }
                        if (valueName.EndsWith(".ApplicationCompany", StringComparison.OrdinalIgnoreCase))
                        {
                            valueName = valueName.Substring(0, valueName.Length - ".ApplicationCompany".Length);
                        }

                        var normalizedPath = PathCleaner.NormalizePath(valueName);
                        
                        bag.Add(new RegistryEntry
                        {
                            Timestamp = DateTimeOffset.MinValue,
                            Source = "MuiCache",
                            Description = "Run Executable",
                            Path = normalizedPath,
                            OtherInfo = ""
                        });
                    }
                    catch { }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return bag.ToList();
        }

        private static async Task<List<RegistryEntry>> ParseWinRARHistoryAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken)
        {
            var bag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var values = key.Values?.ToList() ?? new List<KeyValue>();
            
            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(values, po, value =>
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(value.ValueData))
                        {
                            var normalizedPath = PathCleaner.NormalizePath(value.ValueData);
                            bag.Add(new RegistryEntry
                            {
                                Timestamp = DateTimeOffset.MinValue,
                                Source = "WinRAR History",
                                Description = "Opens an Archive",
                                Path = normalizedPath,
                                OtherInfo = ""
                            });
                        }
                    }
                    catch { }
                });
            }, cancellationToken);
            
            progress?.Report(100);
            return bag.ToList();
        }

        // ROT13 decoding for UserAssist values
        private static string Rot13(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            
            return new string(input.Select(c =>
            {
                if (c >= 'a' && c <= 'z') return (char)(((c - 'a' + 13) % 26) + 'a');
                if (c >= 'A' && c <= 'Z') return (char)(((c - 'A' + 13) % 26) + 'A');
                return c;
            }).ToArray());
        }

        private static async Task<List<RegistryEntry>> ParseValueAsPathAsync(RegistryKey key, IProgress<int> progress, CancellationToken cancellationToken, string specificValueName = null)
        {
            var entriesBag = new System.Collections.Concurrent.ConcurrentBag<RegistryEntry>();
            var valuesToParse = string.IsNullOrEmpty(specificValueName)
                ? key.Values?.ToList() ?? new List<KeyValue>()
                : key.Values?.Where(v => v.ValueName.Equals(specificValueName, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<KeyValue>();

            await Task.Run(() =>
            {
                var po = new ParallelOptions { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount };
                Parallel.ForEach(valuesToParse, po, value =>
                {
                    try
                    {
                        if (value.ValueDataRaw?.Length > MaxValueDataSize)
                            return;

                        var normalizedPath = PathCleaner.NormalizePath(value.ValueData ?? "");

                        entriesBag.Add(new RegistryEntry
                        {
                            Timestamp = DateTimeOffset.MinValue,
                            Path = normalizedPath,
                            OtherInfo = $"Value: {value.ValueName ?? ""}"
                        });
                    }
                    catch { }
                });
            }, cancellationToken);

            progress?.Report(100);
            return entriesBag.ToList();
        }
    }
}