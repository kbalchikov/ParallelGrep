using System.Text;

namespace ParallelGrep.Core
{
    public class SearchParameters
    {
        public Encoding Encoding { get; }
        public string Pattern { get; }
        public int ConcurrencyLevel { get; }
        public bool IgnoreCase { get; }
        public byte[] BytesPattern { get; }

        public SearchParameters(Encoding encoding, string pattern, int concurrencyLevel, bool ignoreCase)
        {
            Encoding = encoding;
            Pattern = pattern;
            ConcurrencyLevel = concurrencyLevel;
            IgnoreCase = ignoreCase;
            BytesPattern = encoding.GetBytes(pattern);
        }
    }
}
