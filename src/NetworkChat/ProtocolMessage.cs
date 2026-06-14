using System.Text;

namespace NetworkChat;

public enum MessageType
{
    Discover,
    Hello,
    HandshakeResponse,
    Text,
    Close
}

public sealed record ProtocolMessage(
    MessageType Type,
    Dictionary<string, string> Headers,
    string Body = "")
{
    public const string Version = "LNC/1";

    public string GetHeader(string name)
    {
        if (!Headers.TryGetValue(name, out var value))
        {
            throw new ProtocolException($"Missing required header '{name}'.");
        }

        return value;
    }

    public Guid GetGuidHeader(string name)
    {
        var value = GetHeader(name);
        return Guid.TryParse(value, out var guid)
            ? guid
            : throw new ProtocolException($"Header '{name}' is not a valid UUID.");
    }

    public DateTimeOffset GetDeadlineHeader()
    {
        var value = GetHeader("Deadline");
        return DateTimeOffset.TryParse(value, out var deadline)
            ? deadline.ToUniversalTime()
            : throw new ProtocolException("Deadline header is not a valid timestamp.");
    }

    public int GetIntHeader(string name)
    {
        var value = GetHeader(name);
        return int.TryParse(value, out var number)
            ? number
            : throw new ProtocolException($"Header '{name}' is not a valid integer.");
    }

    public byte[] ToBytes()
    {
        var builder = new StringBuilder();
        builder.Append(Version).Append(' ').Append(ToWireType(Type)).Append("\r\n");

        foreach (var header in Headers)
        {
            builder.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }

        var bodyBytes = Encoding.UTF8.GetBytes(Body);
        if (bodyBytes.Length > 0 && !Headers.ContainsKey("Length"))
        {
            builder.Append("Length: ").Append(bodyBytes.Length).Append("\r\n");
        }

        builder.Append("\r\n");
        return [.. Encoding.UTF8.GetBytes(builder.ToString()), .. bodyBytes];
    }

    public static ProtocolMessage FromText(string text)
    {
        var separator = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (separator < 0)
        {
            throw new ProtocolException("Message is missing the blank line between headers and body.");
        }

        var headerText = text[..separator];
        var body = text[(separator + 4)..];
        var lines = headerText.Split("\r\n", StringSplitOptions.None);
        if (lines.Length == 0)
        {
            throw new ProtocolException("Message does not contain a start line.");
        }

        var startParts = lines[0].Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (startParts.Length != 2 || startParts[0] != Version)
        {
            throw new ProtocolException($"Start line must be '{Version} <TYPE>'.");
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < lines.Length; index++)
        {
            var line = lines[index];
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                throw new ProtocolException($"Invalid header line: '{line}'.");
            }

            headers[line[..colon]] = line[(colon + 1)..].Trim();
        }

        return new ProtocolMessage(FromWireType(startParts[1]), headers, body);
    }

    public static string ToWireType(MessageType type) => type switch
    {
        MessageType.Discover => "DISCOVER",
        MessageType.Hello => "HELLO",
        MessageType.HandshakeResponse => "HANDSHAKE-RESPONSE",
        MessageType.Text => "TEXT",
        MessageType.Close => "CLOSE",
        _ => throw new ProtocolException($"Unsupported message type '{type}'.")
    };

    public static MessageType FromWireType(string type) => type switch
    {
        "DISCOVER" => MessageType.Discover,
        "HELLO" => MessageType.Hello,
        "HANDSHAKE-RESPONSE" => MessageType.HandshakeResponse,
        "TEXT" => MessageType.Text,
        "CLOSE" => MessageType.Close,
        _ => throw new ProtocolException($"Unknown message type '{type}'.")
    };
}
