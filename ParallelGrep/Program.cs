using CommandLine;
using ParallelGrep.Core;
using System.Text;
using System.Threading.Channels;

namespace ParallelGrep;

public static class Program
{
    public static async Task Main(string[] args)
    {
        try
        {
            await Parser.Default.ParseArguments<CommandLineOptions>(args)
                .WithParsedAsync(RunSearchAsync);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }

    private static async Task<int> RunSearchAsync(CommandLineOptions args)
    {
        Task<TimeSpan>? searchTask;

        string path = args.Path.Trim('\"', '\'');
        string pattern = args.Pattern.Trim('\"', '\'');

        int concLevel = Math.Max(1, Math.Min(Environment.ProcessorCount, args.ConcurrencyLevel));

        var printChannel = Channel.CreateUnbounded<FileResult>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var matchPrinter = new MatchPrinter(printChannel);

        var searchParameters = new SearchParameters(Encoding.UTF8, pattern, concLevel, args.IgnoreCase);
        var searchService = new FileSearchService(printChannel, searchParameters);
        var printTask = matchPrinter.StartAsync(pattern);

        if (File.Exists(path))
        {
            searchTask = searchService.SearchAsync(path);
        }
        else if (Directory.Exists(path))
        {
            searchTask = searchService.SearchAsync(path, "*.*");
        }
        else
        {
            string filePattern = Path.GetFileName(path);
            string? directory = Path.GetDirectoryName(path);

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                Console.WriteLine($"Invalid path {path}");
                return -1;
            }

            if (!filePattern.Contains('*'))
            {
                Console.WriteLine($"Invalid pattern in path {path}");
                return -1;
            }
            searchTask = searchService.SearchAsync(directory, filePattern);
        }

        await Task.WhenAll(searchTask, printTask);
        Console.WriteLine($"Search Elapsed: {searchTask.Result}");
        return 0;
    }
}
