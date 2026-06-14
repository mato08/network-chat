# Local Network Chat

This repository contains a C# implementation of a custom local network chat protocol for the assignment.

## Suggested Commit Steps

Use these as separate commits while you work through the project:

1. Scaffold the C# console project and command-line modes.
2. Add the protocol model, parser, and formatter.
3. Implement UDP discovery for initiator and recipient.
4. Implement TCP handshake validation and rejection cases.
5. Implement text exchange and close messages.
6. Add the protocol specification document.
7. Build and test the final application.

These are logical commit boundaries. Commit them at the time you finish each step; do not fake timestamps.

## Build

```powershell
dotnet build
```

## Run

Open two terminals on the same machine or on two machines in the same local network.

Terminal 1:

```powershell
dotnet run --project src/NetworkChat -- recipient --nickname Bob
```

Terminal 2:

```powershell
dotnet run --project src/NetworkChat -- initiator --to Bob --tcp-port 5050 --deadline-seconds 30
```

The initiator broadcasts a discovery request. The recipient connects back over TCP, sends the handshake UUID, and then the two applications exchange messages one request/response pair at a time.
