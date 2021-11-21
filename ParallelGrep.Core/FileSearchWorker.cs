using Collections.Pooled;
using System.Buffers;
using System.IO.Pipelines;
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

    public async Task RunSearchAsync(Channel<FileStream> channel)
    {
        await foreach (FileStream stream in channel.Reader.ReadAllAsync())
        {
            FileResult fileResult = await ProcessFileAsync(stream);
            await stream.DisposeAsync();
            await printChannel.Writer.WriteAsync(fileResult);
        }
    }

    public async Task<FileResult> ProcessFileAsync(string filePath)
    {
        using var stream = new FileStream(filePath,
            FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ProcessFileAsync(stream);
    }

    public async ValueTask<FileResult> ProcessFileAsync(FileStream stream)
    {
        var reader = PipeReader.Create(stream);

        int lineCount = 0;
        PooledList<Match>? matches = null;

        while (true)
        {
            var readResult = await reader.ReadAsync();
            var buffer = readResult.Buffer;

            FindLinesInBuffer(ref matches, ref buffer, ref lineCount);
            reader.AdvanceTo(buffer.Start, buffer.End);

            if (readResult.IsCompleted)
                break;
        }

        await reader.CompleteAsync();
        return new FileResult(matches, stream.Name);
    }

    private void FindLinesInBuffer(ref PooledList<Match>? matches, ref ReadOnlySequence<byte> buffer, ref int lineCount)
    {
        var reader = new SequenceReader<byte>(buffer);

        while (reader.TryReadTo(out ReadOnlySpan<byte> line, (byte)'\n'))
        {
            lineCount++;

            int matchIndex = ProcessLine(line);
            if (matchIndex >= 0)
            {
                if (matches == null)
                    matches = new PooledList<Match>();

                var owner = MemoryPool<byte>.Shared.Rent(line.Length);
                line.CopyTo(owner.Memory.Span);

                matches.Add(new Match
                {
                    LineNumber = lineCount,
                    Index = matchIndex,
                    Owner = owner,
                });
            }
        }
        buffer = buffer.Slice(reader.Position);
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
                var bytes = ArrayPool<char>.Shared.Rent(source.Length);
                var span = new ReadOnlySpan<char>(bytes);
                int index = span.IndexOf(searchParameters.Pattern.AsSpan(), StringComparison.OrdinalIgnoreCase);
                ArrayPool<char>.Shared.Return(bytes);
                return index;
            }
        }
        else
        {
            return source.IndexOf(searchParameters.BytesPattern);
        }
    }
}
