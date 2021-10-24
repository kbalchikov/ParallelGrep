using System.Buffers;
using System.Threading.Channels;

namespace ParallelGrep.Core;

public class MatchPrinter
{
    private readonly Channel<FileResult> channel;

    public MatchPrinter(Channel<FileResult> channel)
    {
        this.channel = channel;
    }

    public async Task StartAsync(string pattern, CancellationToken token = default)
    {
        await foreach (var file in channel.Reader.ReadAllAsync(token))
        {
            if (file.Matches == null)
                continue;

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"File (total matches: {file.Matches.Count}): {file.Path}");
            foreach (var match in file.Matches)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.Write(match.LineNumber);
                Console.Write(": ");

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write(match.Line[..match.Index]);

                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.Write(match.Line[match.Index..(match.Index + pattern.Length)]);

                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(match.Line[(match.Index + pattern.Length)..match.Line.Length]);
            }

            file.Matches.Dispose();
        }
    }
}
