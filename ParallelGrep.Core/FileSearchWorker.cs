using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;

namespace ParallelGrep.Core;

public class FileSearchWorker
{
    private readonly SearchParameters searchParameters;
    private readonly Channel<FileResult> printChannel;

    public FileSearchWorker(SearchParameters searchParameters, Channel<FileResult> printChannel)
    {
        this.searchParameters = searchParameters;
        this.printChannel = printChannel;
    }

    public async Task RunSearchAsync(Channel<string> channel)
    {
        await foreach (string filePath in channel.Reader.ReadAllAsync())
        {
            FileResult fileResult = await ProcessFileAsync(filePath);
            await printChannel.Writer.WriteAsync(fileResult);
        }
    }

    public async Task<FileResult> ProcessFileAsync(string filePath)
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
                    Line = searchParameters.Encoding.GetString(line)
                });
            }
        }
        buffer = buffer.Slice(reader.Position);
        return matchingLines;
    }

    private int ProcessLine(in ReadOnlySpan<byte> source)
    {
        if (searchParameters.IgnoreCase)
        {
            if (source.Length <= 1024)
            {
                Span<char> sourceChars = stackalloc char[source.Length];
                searchParameters.Encoding.GetChars(source, sourceChars);
                ReadOnlySpan<char> readonlySource = sourceChars;
                return readonlySource.IndexOf(searchParameters.Pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return searchParameters.Encoding.GetString(source).IndexOf(searchParameters.Pattern, StringComparison.OrdinalIgnoreCase);
            }
        }
        else
        {
            return source.IndexOf(searchParameters.BytesPattern);
        }
    }
}
