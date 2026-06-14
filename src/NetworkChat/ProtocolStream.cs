using System.Net.Sockets;
using System.Text;

namespace NetworkChat;

public sealed class ProtocolStream(NetworkStream stream)
{
    private const int MaxHeaderBytes = 8192;
    private const int MaxBodyBytes = 65536;

    public async Task SendAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        var bytes = message.ToBytes();
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public async Task<ProtocolMessage> ReceiveAsync(CancellationToken cancellationToken)
    {
        var received = new List<byte>();
        var buffer = new byte[1];

        while (!EndsWithHeaderSeparator(received))
        {
            if (received.Count >= MaxHeaderBytes)
            {
                throw new ProtocolException("Header is too large.");
            }

            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0)
            {
                throw new IOException("The TCP connection was closed.");
            }

            received.Add(buffer[0]);
        }

        var headerText = Encoding.UTF8.GetString(received.ToArray());
        var headersOnly = ProtocolMessage.FromText(headerText);
        var bodyLength = headersOnly.Headers.TryGetValue("Length", out var lengthValue)
            ? ParseLength(lengthValue)
            : 0;

        if (bodyLength > MaxBodyBytes)
        {
            throw new ProtocolException("Message body is too large.");
        }

        var bodyBuffer = new byte[bodyLength];
        var offset = 0;
        while (offset < bodyLength)
        {
            var count = await stream.ReadAsync(bodyBuffer.AsMemory(offset, bodyLength - offset), cancellationToken);
            if (count == 0)
            {
                throw new IOException("The TCP connection closed before the full body arrived.");
            }

            offset += count;
        }

        return headersOnly with { Body = Encoding.UTF8.GetString(bodyBuffer) };
    }

    private static int ParseLength(string value)
    {
        if (!int.TryParse(value, out var length) || length < 0)
        {
            throw new ProtocolException("Length header must be a non-negative integer.");
        }

        return length;
    }

    private static bool EndsWithHeaderSeparator(List<byte> bytes)
    {
        return bytes.Count >= 4
            && bytes[^4] == '\r'
            && bytes[^3] == '\n'
            && bytes[^2] == '\r'
            && bytes[^1] == '\n';
    }
}
