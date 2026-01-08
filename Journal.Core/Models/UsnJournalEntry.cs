using System;
using System.Collections.Generic;

// USN Journal models and data structures
namespace VortexViewer.Journal.Core.Models
{
    // Represents single USN journal record from NTFS
    public struct UsnJournalEntry
    {
        public int RecordLength { get; set; }
        public long Usn { get; set; }
        public ulong FileReferenceNumber { get; set; }
        public ulong ParentFileReferenceNumber { get; set; }
        public ulong ParentEntryNumber { get; set; }
        public uint ParentSequenceNumber { get; set; }
        public long TimeStamp { get; set; }
        public uint Reason { get; set; }
        public uint FileAttributes { get; set; }
        public string FileName { get; set; }

        // Pool for string interning to reduce memory
        private static readonly Dictionary<string, string> _fileNameInternPool = new Dictionary<string, string>(StringComparer.Ordinal);

        // Parses USN record from raw byte buffer
        public static UsnJournalEntry Parse(byte[] buffer, int offset)
        {
            int recordLength = BitConverter.ToInt32(buffer, offset);
            long usn = BitConverter.ToInt64(buffer, offset + 8);
            ulong fileReferenceNumber = BitConverter.ToUInt64(buffer, offset + 16);
            ulong parentFileReferenceNumber = BitConverter.ToUInt64(buffer, offset + 24);

            // Extract ParentEntryNumber (lower 48 bits) and ParentSequenceNumber (upper 16 bits)
            ulong parentEntryNumber = parentFileReferenceNumber & 0x0000FFFFFFFFFFFF;
            uint parentSequenceNumber = (uint)((parentFileReferenceNumber >> 48) & 0xFFFF);

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
                RecordLength = recordLength,
                Usn = usn,
                FileReferenceNumber = fileReferenceNumber,
                ParentFileReferenceNumber = parentFileReferenceNumber,
                ParentEntryNumber = parentEntryNumber,
                ParentSequenceNumber = parentSequenceNumber,
                TimeStamp = timeStamp,
                Reason = reason,
                FileAttributes = fileAttributes,
                FileName = fileName
            };
        }
    }
}