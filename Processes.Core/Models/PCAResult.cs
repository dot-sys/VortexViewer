namespace Processes.Core.Models
{
    /// Stores executable information from PCA memory traces
    public class PCAResult
    {
        /// Full path to executable file
        public string Path { get; set; }
        /// Modification timestamp of the executable
        public string Modified { get; set; }
        /// Digital signature verification status
        public string Signed { get; set; }
        /// Process source where trace was found
        public string Source { get; set; }

        public PCAResult()
        {
            Path = string.Empty;
            Modified = string.Empty;
            Signed = string.Empty;
            Source = string.Empty;
        }

        public PCAResult(string path, string modified, string signed, string source)
        {
            Path = path;
            Modified = modified;
            Signed = signed;
            Source = source;
        }
    }
}
