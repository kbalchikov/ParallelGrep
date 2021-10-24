using Collections.Pooled;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Threading.Channels;

namespace ParallelGrep.Core;

public readonly record struct Match(int LineNumber, int Index, string Line);
public record class FileResult(PooledList<Match>? Matches, string? Path);

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
        var fileChannel = Channel.CreateBounded<FileStream>(
            new BoundedChannelOptions(searchParameters.ConcurrencyLevel)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            });

        var sw = Stopwatch.StartNew();

        var tasks = new List<Task>(searchParameters.ConcurrencyLevel + 1);

        for (int index = 0; index < searchParameters.ConcurrencyLevel; index++)
        {
            var worker = new FileSearchWorker(searchParameters, printChannel);
            tasks.Add(worker.RunSearchAsync(fileChannel));
        }
        tasks.Add(EnumerateFilesAsync(fileChannel, directory, filePattern));

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

    private async Task EnumerateFilesAsync(Channel<FileStream> channel, string directory, string pattern)
    {
        var files = new FileSystemEnumerable<FileStream?>(directory,
            (ref FileSystemEntry entry) =>
            {
                if (entry.IsDirectory)
                    return null;

                string filePath = Path.Join(
                    entry.Directory,
                    entry.FileName);

                return new FileStream(filePath,
                    FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

            }, new EnumerationOptions { RecurseSubdirectories = true });

        foreach (FileStream? file in files)
        {
            if (file == null)
                continue;

            await channel.Writer.WriteAsync(file);
        }
        channel.Writer.Complete();
    }
}
