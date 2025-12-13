using System.Collections.Generic;

// Extraction output model
namespace Timeline.Core.Models
{
    // Contains timeline entries and hive metadata
    public class ExtractionResult
    {
        public List<RegistryEntry> Entries { get; set; }
        public List<string> ProcessedHives { get; set; }

        public ExtractionResult()
        {
            Entries = new List<RegistryEntry>();
            ProcessedHives = new List<string>();
        }
    }
}