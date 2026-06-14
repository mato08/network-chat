using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NetworkChat;

public static class RecipientApp
{
    public static async Task<int> RunAsync(IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken)
    {
        var nickname = Cli.Required(options, "nickname");
        var udpPort = Cli.IntOption(options, "udp-port", 50000);

        using var udpClient = CreateDiscoveryListener(udpPort);
        Console.WriteLine($"Recipient '{nickname}' listening for UDP discovery on port {udpPort}.");

        while (!cancellationToken.IsCancellationRequested)
        {
            var received = await udpClient.ReceiveAsync(cancellationToken);
            ProtocolMessage discovery;
            try
            {
                discovery = ProtocolMessage.FromText(Encoding.UTF8.GetString(received.Buffer));
            }
            catch (ProtocolException ex)
            {
                Console.WriteLine($"Ignored invalid discovery from {received.RemoteEndPoint}: {ex.Message}");
                continue;
            }

            if (!IsMatchingDiscovery(discovery, nickname))
            {
                continue;
            }

            var requestId = discovery.GetGuidHeader("Request-Id");
            var deadline = discovery.GetDeadlineHeader();
            var tcpPort = discovery.GetIntHeader("Port");
            if (DateTimeOffset.UtcNow > deadline)
            {
                Console.WriteLine($"Ignored expired request {requestId}.");
                continue;
            }

            Console.WriteLine($"Discovery matched from {received.RemoteEndPoint.Address} for request {requestId}.");
            Console.Write("Accept connection? (y/n): ");
            var answer = Console.ReadLine();
            if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Connection declined locally.");
                continue;
            }

            await ConnectAndChatAsync(received.RemoteEndPoint.Address, tcpPort, requestId, cancellationToken);
            return 0;
        }

        return 130;
    }

    private static UdpClient CreateDiscoveryListener(int udpPort)
    {
        var udpClient = new UdpClient(AddressFamily.InterNetwork);
        udpClient.EnableBroadcast = true;
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));
        return udpClient;
    }

    private static bool IsMatchingDiscovery(ProtocolMessage discovery, string nickname)
    {
        if (discovery.Type != MessageType.Discover)
        {
            return false;
        }

        return discovery.Headers.TryGetValue("To", out var target)
            && string.Equals(target, nickname, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ConnectAndChatAsync(IPAddress initiatorAddress, int tcpPort, Guid requestId, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        Console.WriteLine($"Connecting to initiator at {initiatorAddress}:{tcpPort}.");
        await tcpClient.ConnectAsync(initiatorAddress, tcpPort, cancellationToken);

        var protocol = new ProtocolStream(tcpClient.GetStream());
        await protocol.SendAsync(new ProtocolMessage(
            MessageType.Hello,
            new Dictionary<string, string> { ["Request-Id"] = requestId.ToString() }), cancellationToken);

        var response = await protocol.ReceiveAsync(cancellationToken);
        if (response.Type != MessageType.HandshakeResponse)
        {
            throw new ProtocolException("Expected HANDSHAKE-RESPONSE from initiator.");
        }

        var status = response.GetHeader("Status");
        if (!string.Equals(status, "ACCEPT", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"Handshake rejected: {response.GetHeader("Reason")}");
            return;
        }

        Console.WriteLine("Handshake accepted. Waiting for initiator messages.");
        await ChatAsRecipientAsync(protocol, requestId, cancellationToken);
    }

    private static async Task ChatAsRecipientAsync(ProtocolStream protocol, Guid requestId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var incoming = await protocol.ReceiveAsync(cancellationToken);
            if (incoming.Type == MessageType.Close)
            {
                Console.WriteLine($"Initiator closed: {incoming.Body}");
                return;
            }

            if (incoming.Type != MessageType.Text)
            {
                throw new ProtocolException("Expected TEXT or CLOSE from initiator.");
            }

            Console.WriteLine($"Initiator: {incoming.Body}");
            Console.Write("Reply: ");
            var reply = Console.ReadLine();
            if (reply is null || reply.Equals("/quit", StringComparison.OrdinalIgnoreCase))
            {
                await protocol.SendAsync(new ProtocolMessage(
                    MessageType.Close,
                    new Dictionary<string, string> { ["Request-Id"] = requestId.ToString() },
                    "Recipient closed the connection."), cancellationToken);
                Console.WriteLine("Close request sent.");
                return;
            }

            await protocol.SendAsync(new ProtocolMessage(
                MessageType.Text,
                new Dictionary<string, string> { ["Request-Id"] = requestId.ToString() },
                reply), cancellationToken);
        }
    }
}
