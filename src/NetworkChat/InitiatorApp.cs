using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NetworkChat;

public static class InitiatorApp
{
    public static async Task<int> RunAsync(IReadOnlyDictionary<string, string> options, CancellationToken cancellationToken)
    {
        var recipient = Cli.Required(options, "to");
        var tcpPort = Cli.IntOption(options, "tcp-port", 5050);
        var udpPort = Cli.IntOption(options, "udp-port", 50000);
        var deadlineSeconds = Cli.IntOption(options, "deadline-seconds", 30);
        var requestId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(deadlineSeconds);

        var listener = new TcpListener(IPAddress.Any, tcpPort);
        listener.Start();

        try
        {
            await SendDiscoveryAsync(recipient, requestId, deadline, tcpPort, udpPort, cancellationToken);
            Console.WriteLine($"Discovery sent for '{recipient}' with request {requestId}.");
            Console.WriteLine($"Waiting for TCP connection on port {tcpPort} until {deadline:O}.");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(deadline - DateTimeOffset.UtcNow);

            using var tcpClient = await listener.AcceptTcpClientAsync(timeoutCts.Token);
            Console.WriteLine($"TCP connection accepted from {tcpClient.Client.RemoteEndPoint}.");

            var protocol = new ProtocolStream(tcpClient.GetStream());
            var hello = await protocol.ReceiveAsync(cancellationToken);
            var accepted = ValidateHello(hello, requestId, deadline, out var reason);
            await protocol.SendAsync(new ProtocolMessage(
                MessageType.HandshakeResponse,
                new Dictionary<string, string>
                {
                    ["Request-Id"] = requestId.ToString(),
                    ["Status"] = accepted ? "ACCEPT" : "REJECT",
                    ["Reason"] = reason
                }), cancellationToken);

            if (!accepted)
            {
                Console.WriteLine($"Handshake rejected: {reason}");
                return 1;
            }

            Console.WriteLine("Handshake accepted. Type messages, or /quit to close.");
            await ChatAsInitiatorAsync(protocol, requestId, cancellationToken);
            return 0;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task SendDiscoveryAsync(
        string recipient,
        Guid requestId,
        DateTimeOffset deadline,
        int tcpPort,
        int udpPort,
        CancellationToken cancellationToken)
    {
        using var udpClient = new UdpClient();
        udpClient.EnableBroadcast = true;

        var discovery = new ProtocolMessage(
            MessageType.Discover,
            new Dictionary<string, string>
            {
                ["To"] = recipient,
                ["Deadline"] = deadline.ToUniversalTime().ToString("O"),
                ["Port"] = tcpPort.ToString(),
                ["Request-Id"] = requestId.ToString()
            });

        var bytes = discovery.ToBytes();
        await udpClient.SendAsync(bytes, new IPEndPoint(IPAddress.Broadcast, udpPort), cancellationToken);
    }

    private static bool ValidateHello(ProtocolMessage hello, Guid requestId, DateTimeOffset deadline, out string reason)
    {
        if (hello.Type != MessageType.Hello)
        {
            reason = "Expected HELLO message.";
            return false;
        }

        try
        {
            if (hello.GetGuidHeader("Request-Id") != requestId)
            {
                reason = "UUID does not match an active request.";
                return false;
            }
        }
        catch (ProtocolException ex)
        {
            reason = ex.Message;
            return false;
        }

        if (DateTimeOffset.UtcNow > deadline)
        {
            reason = "Deadline has expired.";
            return false;
        }

        reason = "OK";
        return true;
    }

    private static async Task ChatAsInitiatorAsync(ProtocolStream protocol, Guid requestId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("> ");
            var input = Console.ReadLine();
            if (input is null || input.Equals("/quit", StringComparison.OrdinalIgnoreCase))
            {
                await protocol.SendAsync(new ProtocolMessage(
                    MessageType.Close,
                    new Dictionary<string, string> { ["Request-Id"] = requestId.ToString() },
                    "Initiator closed the connection."), cancellationToken);
                Console.WriteLine("Close request sent.");
                return;
            }

            await protocol.SendAsync(new ProtocolMessage(
                MessageType.Text,
                new Dictionary<string, string> { ["Request-Id"] = requestId.ToString() },
                input), cancellationToken);

            var response = await protocol.ReceiveAsync(cancellationToken);
            if (response.Type == MessageType.Close)
            {
                Console.WriteLine($"Recipient closed: {response.Body}");
                return;
            }

            if (response.Type != MessageType.Text)
            {
                throw new ProtocolException("Expected TEXT response before sending the next message.");
            }

            Console.WriteLine($"Recipient: {response.Body}");
        }
    }
}
