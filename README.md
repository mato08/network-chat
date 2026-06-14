# Local Network Chat

This repository contains a C# implementation of a custom local network chat protocol for the assignment.

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
