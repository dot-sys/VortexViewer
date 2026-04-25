using System;
using System.Collections.Generic;
using System.Text;
using VortexViewer.Journal.Core.Util;
using VortexViewer.Journal.Core.Models;

// Core journal processing and types
namespace VortexViewer.Core
{
    // Parses raw journal into entries
    public static class JournalParser
    {
        // File create flag mask
        private const uint FILE_CREATE      = 0x00000100;
        // File delete flag mask
        private const uint FILE_DELETE      = 0x00000200;
        // Rename old name flag mask
        private const uint RENAME_OLD_NAME  = 0x00001000;

        // Progress extract constants
        private const int EXTRACT_START = 0,  EXTRACT_END = 10;
        // Progress MFT constants
        private const int MFT_START     = 11, MFT_END     = 30;
        // Progress journal constants
        private const int JOURNAL_START = 31, JOURNAL_END = 60;
        // Progress parse constants
        private const int PARSE_START   = 61, PARSE_END   = 95;

        // Parses the journal entries
        public static List<JournalEntry> ParseJournal(string driveLetter, Action<int, int, string> progressCallback = null)
        {
            ReportProgress(progressCallback, EXTRACT_START, EXTRACT_END, "Reading USN Journal", 2);
            var rawEntries = UsnJournalApi.ReadJournalEntries(driveLetter);

            ReportProgress(progressCallback, MFT_START, MFT_END, "Scanning MFT", 2);
            var stateMap = MftUtil.GetInitialStateMap(driveLetter);

            string drive = driveLetter.TrimEnd(':', '\\');

            var result = new List<JournalEntry>(rawEntries.Count);

            ReportProgress(progressCallback, JOURNAL_START, JOURNAL_END, "Building Journal", 2);

            int total = rawEntries.Count;
            int parseStep = Math.Max(1, total / Math.Max(1, PARSE_END - PARSE_START));

            for (int i = total - 1; i >= 0; i--)
            {
                var entry = rawEntries[i];

                ulong fileEntry   = entry.FileReferenceNumber       & 0x0000FFFFFFFFFFFF;
                ulong parentEntry = entry.ParentFileReferenceNumber & 0x0000FFFFFFFFFFFF;

                string fullPath = BuildHistoricalPath(
                    parentEntry,
                    entry.FileName,
                    stateMap,
                    drive);

                var reasonString = JournalEntry.GetReasonString(entry.Reason);
                result.Add(new JournalEntry(fullPath, reasonString, entry.TimeStamp));

                bool isDelete    = (entry.Reason & FILE_DELETE)     != 0;
                bool isRenameOld = (entry.Reason & RENAME_OLD_NAME) != 0;
                bool isCreate    = (entry.Reason & FILE_CREATE)     != 0;
                bool isDirectory = (entry.FileAttributes & 0x10)    != 0;

                if (isDelete || isRenameOld)
                {
                    stateMap[fileEntry] = new HistoricalState
                    {
                        ParentFRN   = parentEntry,
                        Name        = entry.FileName,
                        IsDirectory = isDirectory
                    };
                }
                else if (isCreate)
                {
                    stateMap.Remove(fileEntry);
                }

                if (progressCallback != null && ((total - 1 - i) % parseStep == 0))
                {
                    int processed = total - 1 - i;
                    int percent = PARSE_START + (int)((PARSE_END - PARSE_START) * processed / (double)total);
                    progressCallback(percent, 100, "Parsing FullPaths");
                }
            }

            progressCallback?.Invoke(100, 100, "Ready");

            return result;
        }

        // Builds the historical path
        private static string BuildHistoricalPath(
            ulong parentFrn,
            string fileName,
            Dictionary<ulong, HistoricalState> stateMap,
            string drive)
        {
            var segments = new List<string>(8);
            ulong current = parentFrn;
            int guard = 256;

            while (current != 5 && current != 0 && guard-- > 0)
            {
                if (!stateMap.TryGetValue(current, out var state))
                    break;

                if (!string.IsNullOrEmpty(state.Name))
                    segments.Add(state.Name);

                if (state.ParentFRN == current)
                    break;

                current = state.ParentFRN;
            }

            var sb = new StringBuilder(260);
            sb.Append(drive);
            sb.Append(@":\");

            for (int s = segments.Count - 1; s >= 0; s--)
            {
                sb.Append(segments[s]);
                sb.Append('\\');
            }

            sb.Append(fileName);
            return sb.ToString();
        }

        // Reports the parsing progress
        private static void ReportProgress(Action<int, int, string> cb, int start, int end, string message, int step)
        {
            if (cb == null) return;
            for (int i = start; i <= end; i += step)
                cb(i, 100, message);
        }
    }
}