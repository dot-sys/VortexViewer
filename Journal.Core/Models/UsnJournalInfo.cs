// USN Journal data structures
namespace VortexViewer.Journal.Core.Models
{
    // Journal configuration details
    public class UsnJournalInfo
    {
        // Unique journal identifier
        public ulong JournalId { get; set; }
        // Maximum journal growth size
        public ulong MaximumSize { get; set; }
        // Size increment for expansion
        public ulong AllocationDelta { get; set; }
        // Oldest sequence number
        public ulong FirstUsn { get; set; }
    }
}
