using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;

namespace ParallelGrep.Core;

public readonly record struct Match(int LineNumber, int Index, string Line);
public record class FileResult(List<Match> Matches, string? Path);

public class FileSearchService
{
    private readonly Channel<FileResult> printChannel;
    private readonly Encoding encoding;
    private readonly bool ignoreCase;
    private readonly string pattern;
    private readonly byte[] bytesPattern;
    private readonly int concurrencyLevel;

    public FileSearchService(Channel<FileResult> printChannel, string pattern, bool ignoreCase, int concurrencyLevel)
    {
        this.printChannel = printChannel;
        encoding = Encoding.UTF8;
        this.ignoreCase = ignoreCase;
        this.concurrencyLevel = concurrencyLevel;
        this.pattern = pattern;
        bytesPattern = encoding.GetBytes(pattern);
    }

    public async Task<TimeSpan> SearchAsync(string directory, string filePattern)
    {
        var directoryChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });
        var sw = Stopwatch.StartNew();

        var tasks = new List<Task>(concurrencyLevel + 1);

        for (int index = 0; index < concurrencyLevel; index++)
            tasks.Add(RunSearchAsync(directoryChannel));

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
        var fileResult = await ProcessFileAsync(file);
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

    private async Task RunSearchAsync(Channel<string> channel)
    {
        await foreach (string filePath in channel.Reader.ReadAllAsync())
        {
            FileResult fileResult = await ProcessFileAsync(filePath);
            await printChannel.Writer.WriteAsync(fileResult);
        }
    }

    private async Task<FileResult> ProcessFileAsync(string filePath)
    {
        using var stream = new FileStream(filePath,
            FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var reader = PipeReader.Create(stream);

        var matches = new List<Match>();
        int lineCount = 0;

        while (true)
        {
            var readResult = await reader.ReadAsync();
            var buffer = readResult.Buffer;

            matches.AddRange(FindLinesInBuffer(ref buffer, ref lineCount));
            reader.AdvanceTo(buffer.Start, buffer.End);

            if (readResult.IsCompleted)
                break;
        }

        await reader.CompleteAsync();
        return new FileResult(matches, filePath);
    }

    private List<Match> FindLinesInBuffer(ref ReadOnlySequence<byte> buffer, ref int lineCount)
    {
        var matchingLines = new List<Match>();
        var reader = new SequenceReader<byte>(buffer);

        while (reader.TryReadTo(out ReadOnlySpan<byte> line, (byte)'\n'))
        {
            lineCount++;

            int matchIndex = ProcessLine(line);
            if (matchIndex >= 0)
            {
                matchingLines.Add(new Match
                {
                    LineNumber = lineCount,
                    Index = matchIndex,
                    Line = encoding.GetString(line)
                });
            }
        }
        buffer = buffer.Slice(reader.Position);
        return matchingLines;
    }

    private int ProcessLine(in ReadOnlySpan<byte> source)
    {
        if (ignoreCase)
        {
            if (source.Length <= 1024)
            {
                Span<char> sourceChars = stackalloc char[source.Length];
                encoding.GetChars(source, sourceChars);
                ReadOnlySpan<char> readonlySource = sourceChars;
                return readonlySource.IndexOf(pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return encoding.GetString(source).IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            return source.IndexOf(bytesPattern);
        }
    }
}
