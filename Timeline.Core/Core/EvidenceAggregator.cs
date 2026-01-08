using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Timeline.Core.Models;
using Timeline.Core.Util;

// Timeline evidence aggregation and processing pipeline
namespace Timeline.Core.Core
{
    // Combines all parser outputs into timeline
    public static class EvidenceAggregator
    {
        public static async Task<List<RegistryEntry>> AggregateAllEvidenceAsync(List<List<RegistryEntry>> allParserResults, bool uppercaseResults = true, IProgress<string> progress = null)
        {
            progress?.Report("Aggregating evidence from all parsers...");
            
            int totalCount = 0;
            foreach (var resultList in allParserResults)
            {
                totalCount += resultList.Count;
            }
            
            progress?.Report($"Total entries from parsers: {totalCount:N0}");
            
            var finalEntries = new List<RegistryEntry>(totalCount);

            foreach (var resultList in allParserResults)
            {
                finalEntries.AddRange(resultList);
            }
            
            allParserResults.Clear();

            int countBeforeClean = finalEntries.Count;
            progress?.Report($"Cleaning {finalEntries.Count:N0} entry paths...");

            await Task.Run(() => PathCleaner.CleanAllPaths(finalEntries)).ConfigureAwait(false);

            progress?.Report($"After path cleaning: {countBeforeClean:N0} → {finalEntries.Count:N0}");

            progress?.Report("Applying case formatting...");

            await Task.Run(() =>
            {
                if (uppercaseResults)
                {
                    PathCleaner.UppercaseAllStrings(finalEntries);
                }
                else
                {
                    PathCleaner.TitleCaseAllStrings(finalEntries);
                }
                }).ConfigureAwait(false);

            int countBeforeAnalysis = finalEntries.Count;
            progress?.Report("Analyzing files (status, signatures, modifications)...");
            
            await Task.Run(() =>
            {
                var processedCount = 0;
                var totalEntries = finalEntries.Count;
                
                foreach (var entry in finalEntries)
                {
                    // FIXED: Skip status/signature analysis for historical trace sources
                    // Shellbags and Recent items are historical artifacts - status is always unknown
                    bool isHistoricalTrace = entry.Source == "Shellbag" || entry.Source == "Recent";
                    
                    if (isHistoricalTrace)
                    {
                        // For historical traces, don't check file existence or signatures
                        entry.Modified = "";
                        entry.Signed = "";
                        entry.CN = "";
                        entry.OU = "";
                        entry.S = "";
                        entry.Serial = "";
                    }
                    else
                    {
                        var analysisResult = FileStatusDetector.AnalyzeFile(entry.Path);
                        
                        // Set modification status (will be "Deleted", "Unknown", "Renamed", or empty for normal files)
                        entry.Modified = analysisResult.Modified;
                        
                        // Set signature information
                        entry.Signed = analysisResult.SignatureInfo.Status;
                        entry.CN = analysisResult.SignatureInfo.CN;
                        entry.OU = analysisResult.SignatureInfo.OU;
                        entry.S = analysisResult.SignatureInfo.S;
                        entry.Serial = analysisResult.SignatureInfo.Serial;
                        
                        if (!string.IsNullOrEmpty(analysisResult.PathStatus) && analysisResult.PathStatus == "Present")
                        {
                            if (string.IsNullOrEmpty(entry.OtherInfo))
                            {
                                entry.OtherInfo = analysisResult.PathStatus;
                            }
                            else
                            {
                                entry.OtherInfo += "; " + analysisResult.PathStatus;
                            }
                        }
                    }
                    
                    processedCount++;
                    if (processedCount % 500 == 0)
                    {
                        progress?.Report($"Analyzed {processedCount}/{totalEntries} files...");
                    }
                }
                
                progress?.Report($"File analysis complete ({totalEntries} files)");
            }).ConfigureAwait(false);

            progress?.Report($"After file analysis: {countBeforeAnalysis:N0} → {finalEntries.Count:N0}");

            int countBeforeSort = finalEntries.Count;
            progress?.Report($"Sorting {finalEntries.Count:N0} entries by timestamp...");

            await Task.Run(() => 
            {
                finalEntries.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
            }).ConfigureAwait(false);

            progress?.Report($"Final entry count: {finalEntries.Count:N0}");
            progress?.Report("Aggregation complete");
            
            return finalEntries;
        }
    }
}