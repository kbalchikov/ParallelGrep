using System.Diagnostics;
using System.Threading.Channels;

namespace ParallelGrep.Core;

public readonly record struct Match(int LineNumber, int Index, string Line);
public record class FileResult(List<Match> Matches, string? Path);

public class FileSearchService
{
    private readonly Channel<FileResult> printChannel;
    private readonly SearchParameters searchParameters;

    public FileSearchService(Channel<FileResult> printChannel, SearchParameters searchParameters)
    {
        this.printChannel = printChannel;
        this.searchParameters = searchParameters;
    }

    public async Task<TimeSpan> SearchAsync(string directory, string filePattern)
    {
        var directoryChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });
        var sw = Stopwatch.StartNew();

        var tasks = new List<Task>(searchParameters.ConcurrencyLevel + 1);

        for (int index = 0; index < searchParameters.ConcurrencyLevel; index++)
        {
            var worker = new FileSearchWorker(searchParameters, printChannel);
            tasks.Add(worker.RunSearchAsync(directoryChannel));
        }

        tasks.Add(EnumerateDirectoriesAsync(
            directoryChannel,
            Directory.EnumerateFiles(directory, filePattern, SearchOption.AllDirectories)));

        await Task.WhenAll(tasks);
        printChannel.Writer.Complete();
        return sw.Elapsed;
    }

    public async Task<TimeSpan> SearchAsync(string file)
    {
        var sw = Stopwatch.StartNew();
        var worker = new FileSearchWorker(searchParameters, printChannel);
        var fileResult = await worker.ProcessFileAsync(file);
        await printChannel.Writer.WriteAsync(fileResult);
        sw.Stop();

        printChannel.Writer.Complete();
        return sw.Elapsed;
    }

    private async Task EnumerateDirectoriesAsync(Channel<string> channel, IEnumerable<string> enumerable)
    {
        var enumerator = enumerable.GetEnumerator();
        while (enumerator.MoveNext())
        {
            await channel.Writer.WriteAsync(enumerator.Current);
        }
        channel.Writer.Complete();
    }
}
