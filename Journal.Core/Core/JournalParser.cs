using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using VortexViewer.Journal.Core.Util;
using VortexViewer.Journal.Core.Models;

// Core journal processing and data types
namespace VortexViewer.Core
{
    // Parses raw USN journal into entries
    public static class JournalParser
    {
        // Progress range constants for reporting
        private const int EXTRACT_START = 0, EXTRACT_END = 20;
        private const int JOURNAL_START = 21, JOURNAL_END = 60;
        private const int PARSE_START = 61, PARSE_END = 80;
        private const int SORT_START = 81, SORT_END = 100;

        // Main entry point for journal parsing
        public static List<JournalEntry> ParseJournal(string driveLetter, Action<int, int, string> progressCallback = null)
        {
            var swTotal = Stopwatch.StartNew();

            ReportProgress(progressCallback, EXTRACT_START, EXTRACT_END, "Starting Extraction", 2);

            var swUsn = Stopwatch.StartNew();
            var rawEntries = UsnJournalApi.ReadJournalEntries(driveLetter);
            swUsn.Stop();
            
            ReportProgressForCollection(progressCallback, rawEntries.Count, JOURNAL_START, JOURNAL_END, "Reading Journals");

            var swMft = Stopwatch.StartNew();
            var neededFrns = new HashSet<ulong>(
                rawEntries.Select(e => e.FileReferenceNumber)
                .Concat(rawEntries.Select(e => e.ParentFileReferenceNumber))
            );
            var mftData = MftUtil.BuildParentPathMap(driveLetter, neededFrns);
            swMft.Stop();

            var result = new JournalEntry[rawEntries.Count];
            int total = rawEntries.Count;
            int parseStep = Math.Max(1, total / (PARSE_END - PARSE_START));
            
            for (int i = 0; i < total; i++)
            {
                var entry = rawEntries[i];
                var fullPath = JournalEntry.BuildFullPath(
                    entry.FileReferenceNumber,
                    mftData.ResolvedPaths,
                    mftData.ParentFrnMap,
                    mftData.Names,
                    entry.FileName,
                    driveLetter);
                var reasonString = JournalEntry.GetReasonString(entry.Reason);
                result[i] = new JournalEntry(fullPath, reasonString, entry.TimeStamp);

                if (progressCallback != null && (i % parseStep == 0))
                {
                    int percent = PARSE_START + (int)((PARSE_END - PARSE_START) * i / (double)total);
                    progressCallback(percent, 100, "Parsing FullPaths");
                }
            }
            progressCallback?.Invoke(PARSE_END, 100, "Parsing FullPaths");

            ReportProgress(progressCallback, SORT_START, SORT_END, "Sorting Entries", 1);

            mftData.ResolvedPaths.Clear();
            mftData.ParentFrnMap.Clear();
            mftData.Names.Clear();
            swTotal.Stop();

            var sorted = result.OrderByDescending(e => e.FileTime).ToList();
            progressCallback?.Invoke(100, 100, "Ready");
            return sorted;
        }

        // Reports progress in specified range
        private static void ReportProgress(Action<int, int, string> progressCallback, int start, int end, string message, int step)
        {
            for (int i = start; i <= end; i += step)
            {
                progressCallback?.Invoke(i, 100, message);
            }
        }

        // Reports progress based on collection size
        private static void ReportProgressForCollection(Action<int, int, string> progressCallback, int collectionCount, int start, int end, string message)
        {
            int steps = Math.Max(1, collectionCount / (end - start));
            for (int i = 0; i < collectionCount; i += steps)
            {
                int percent = start + (int)((end - start) * i / (double)collectionCount);
                progressCallback?.Invoke(percent, 100, message);
            }
            progressCallback?.Invoke(end, 100, message);
        }
    }
}