namespace NetworkChat;

public static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            PrintUsage();
            return args.Length == 0 ? 1 : 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        try
        {
            var options = ParseOptions(args.Skip(1).ToArray());
            return args[0].ToLowerInvariant() switch
            {
                "initiator" => await InitiatorApp.RunAsync(options, cts.Token),
                "recipient" => await RecipientApp.RunAsync(options, cts.Token),
                _ => UnknownMode(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex) when (ex is ProtocolException or IOException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    public static string Required(IReadOnlyDictionary<string, string> options, string name)
    {
        return options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"Missing required option '--{name}'.");
    }

    public static int IntOption(IReadOnlyDictionary<string, string> options, string name, int defaultValue)
    {
        if (!options.TryGetValue(name, out var value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var number)
            ? number
            : throw new InvalidOperationException($"Option '--{name}' must be an integer.");
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected argument '{name}'.");
            }

            if (index + 1 >= args.Length)
            {
                throw new InvalidOperationException($"Option '{name}' requires a value.");
            }

            result[name[2..]] = args[++index];
        }

        return result;
    }

    private static int UnknownMode(string mode)
    {
        Console.Error.WriteLine($"Unknown mode '{mode}'.");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Local Network Chat");
        Console.WriteLine();
        Console.WriteLine("Recipient:");
        Console.WriteLine("  dotnet run --project src/NetworkChat -- recipient --nickname Bob [--udp-port 50000]");
        Console.WriteLine();
        Console.WriteLine("Initiator:");
        Console.WriteLine("  dotnet run --project src/NetworkChat -- initiator --to Bob [--tcp-port 5050] [--deadline-seconds 30] [--udp-port 50000]");
        Console.WriteLine();
        Console.WriteLine("During chat, type /quit to send a close request.");
    }
}
