// USN Journal models and data structures
namespace VortexViewer.Journal.Core.Models
{
    // Represents the known state of a file/directory at a given point in time
    public struct HistoricalState
    {
        // FRN of the parent directory
        public ulong ParentFRN;
        // Name of the file or directory
        public string Name;
        // Whether this entry represents a directory
        public bool IsDirectory;
    }
}
