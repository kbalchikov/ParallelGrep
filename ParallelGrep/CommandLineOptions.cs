using CommandLine;

namespace ParallelGrep;

public class CommandLineOptions
{
    [Value(index: 0, Required = true, HelpText = "pattern to search")]
    public string Pattern { get; set; } = string.Empty;

    [Value(index: 1, Required = true, HelpText = "directory or file to search")]
    public string Path { get; set; } = string.Empty;

    [Option(shortName: 'i', longName: "ignore-case", Default = false, HelpText = "ignore case distinctions")]
    public bool IgnoreCase { get; set; }

    [Option(shortName: 'c', longName: "concurrency-level", Default = 1, HelpText = "defines concurrency level")]
    public int ConcurrencyLevel { get; set; }
}
