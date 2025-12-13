// USN Journal models and data structures
namespace VortexViewer.Journal.Core.Models
{
    // Information about USN journal configuration
    public class UsnJournalInfo
    {
        // Drive letter of this journal
        public string DriveLetter { get; set; }
        // Unique identifier for this journal
        public ulong JournalId { get; set; }
        // Maximum size journal can grow to
        public ulong MaximumSize { get; set; }
        // Size increment for journal expansion
        public ulong AllocationDelta { get; set; }
        // Oldest USN in current journal
        public ulong FirstUsn { get; set; }
        // Next USN to be written
        public ulong NextUsn { get; set; }
    }
}
