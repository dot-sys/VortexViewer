using System;
using System.Collections.Generic;

// USN Journal models
namespace VortexViewer.Journal.Core.Models
{
    // Single USN journal record
    public struct UsnJournalEntry
    {
        // Reference number of file
        public ulong FileReferenceNumber { get; set; }
        // Reference number of parent
        public ulong ParentFileReferenceNumber { get; set; }
        // Time stamp of entry
        public long TimeStamp { get; set; }
        // Reason for the entry
        public uint Reason { get; set; }
        // Attributes of the file
        public uint FileAttributes { get; set; }
        // Name of the file
        public string FileName { get; set; }

        // Pool for file names
        private static readonly Dictionary<string, string> _fileNameInternPool = new Dictionary<string, string>(StringComparer.Ordinal);

        // Parses USN record
        public static UsnJournalEntry Parse(byte[] buffer, int offset)
        {
            ulong fileReferenceNumber = BitConverter.ToUInt64(buffer, offset + 8);
            ulong parentFileReferenceNumber = BitConverter.ToUInt64(buffer, offset + 16);

            long timeStamp = BitConverter.ToInt64(buffer, offset + 32);
            uint reason = BitConverter.ToUInt32(buffer, offset + 40);
            uint fileAttributes = BitConverter.ToUInt32(buffer, offset + 52);
            short fileNameLength = BitConverter.ToInt16(buffer, offset + 56);
            short fileNameOffset = BitConverter.ToInt16(buffer, offset + 58);
            string fileName = System.Text.Encoding.Unicode.GetString(buffer, offset + fileNameOffset, fileNameLength);

            lock (_fileNameInternPool)
            {
                if (_fileNameInternPool.TryGetValue(fileName, out var interned))
                    fileName = interned;
                else
                    _fileNameInternPool[fileName] = fileName;
            }

            return new UsnJournalEntry
            {
                FileReferenceNumber = fileReferenceNumber,
                ParentFileReferenceNumber = parentFileReferenceNumber,
                TimeStamp = timeStamp,
                Reason = reason,
                FileAttributes = fileAttributes,
                FileName = fileName
            };
        }
    }
}