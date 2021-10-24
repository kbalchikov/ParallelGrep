using System.Buffers;
using System.Text;
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
        var stream = Console.OpenStandardOutput();
        var dotsBytes = Encoding.UTF8.GetBytes(": ");

        await foreach (var file in channel.Reader.ReadAllAsync(token))
        {
            if (file.Matches == null)
                continue;

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            await stream.WriteAsync(Encoding.UTF8.GetBytes($"File (total matches: {file.Matches.Count}): {file.Path}\n"));

            Console.WriteLine();
            foreach (var match in file.Matches)
            {
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                await stream.WriteAsync(Encoding.UTF8.GetBytes(match.LineNumber.ToString()));
                await stream.WriteAsync(dotsBytes);

                Console.ForegroundColor = ConsoleColor.Gray;
                await stream.WriteAsync(match.Owner.Memory[..match.Index]);

                Console.ForegroundColor = ConsoleColor.DarkRed;
                await stream.WriteAsync(match.Owner.Memory[match.Index..(match.Index + pattern.Length)]);

                Console.ForegroundColor = ConsoleColor.Gray;
                await stream.WriteAsync(match.Owner.Memory[(match.Index + pattern.Length)..match.Owner.Memory.Length]);
                await stream.WriteAsync(new byte[] { (byte)'\n'});

                match.Owner.Dispose();
            }
            file.Matches.Dispose();
        }
    }
}
