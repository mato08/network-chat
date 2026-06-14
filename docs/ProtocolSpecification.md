# Local Network Chat Protocol Specification

## 1. Overview

Local Network Chat Protocol version 1 (`LNC/1`) is a small application-layer protocol for discovering and starting a one-to-one chat on a local network. It uses UDP broadcast for discovery and TCP for reliable handshaking and message exchange.

The protocol is inspired by text-based protocols such as HTTP and SMTP:

- A start line identifies the protocol version and message type.
- Headers carry structured metadata.
- A blank line separates headers from the optional message body.
- UTF-8 is used for all encoded text.

## 2. Design Goals

| Goal | Description |
| --- | --- |
| Human-readable messages | Frames can be inspected during testing. |
| Explicit message types | Every frame starts with a type prefix. |
| Request validation | The UUID and deadline prevent accepting stale or unrelated connections. |
| Simple turn-taking | The sender must receive one response before sending the next text message. |
| Clear failure behavior | Invalid UUIDs, expired deadlines, malformed frames, and unexpected message types are rejected. |

## 3. Transport Use

| Phase | Transport | Purpose |
| --- | --- | --- |
| Discovery | UDP broadcast | Initiator announces a request to the local network. |
| Handshake | TCP | Recipient connects back and proves it received the UUID. |
| Chat | TCP | Text and close messages are exchanged reliably. |

Default UDP discovery port: `50000`

Default initiator TCP listening port: `5050`

## 4. General Message Format

All protocol messages use this format:

```text
LNC/1 <TYPE>\r\n
Header-Name: Header Value\r\n
Another-Header: Another Value\r\n
\r\n
optional UTF-8 body
```

Rules:

- The protocol version is exactly `LNC/1`.
- Header names are ASCII words using hyphens when needed.
- Header values are UTF-8 text without raw CRLF characters.
- Header lookup is case-insensitive.
- `Length` is required when a TCP message has a body.
- `Length` is the UTF-8 byte length of the body.
- Timestamps use ISO 8601 UTC format, for example `2026-06-14T12:00:00.0000000Z`.
- UUIDs use standard GUID text format.

## 5. Message Types

### 5.1 DISCOVER

Sent by the Initiator over UDP broadcast.

| Header | Required | Description |
| --- | --- | --- |
| `To` | Yes | Recipient nickname. |
| `Deadline` | Yes | UTC time after which the request is invalid. |
| `Port` | Yes | TCP port where the Initiator is listening. |
| `Request-Id` | Yes | Random UUID for this request. |

Example:

```text
LNC/1 DISCOVER
To: Bob
Deadline: 2026-06-14T12:00:30.0000000Z
Port: 5050
Request-Id: 8d4f8a8e-705a-4657-91a8-59be1e5525e0

```

### 5.2 HELLO

Sent by the Recipient over TCP immediately after connecting to the Initiator.

| Header | Required | Description |
| --- | --- | --- |
| `Request-Id` | Yes | UUID copied from the matching discovery message. |

### 5.3 HANDSHAKE-RESPONSE

Sent by the Initiator over TCP after validating `HELLO`.

| Header | Required | Description |
| --- | --- | --- |
| `Request-Id` | Yes | UUID being accepted or rejected. |
| `Status` | Yes | `ACCEPT` or `REJECT`. |
| `Reason` | Yes | Human-readable result reason. |

Acceptance means the UUID matched and the deadline had not expired. Rejection means the UUID was invalid, the deadline had expired, or the frame was not a valid handshake.

### 5.4 TEXT

Sent over TCP after the handshake succeeds.

| Header | Required | Description |
| --- | --- | --- |
| `Request-Id` | Yes | Active chat UUID. |
| `Length` | Yes | UTF-8 body length in bytes. |

The body contains the chat text.

### 5.5 CLOSE

Sent over TCP to request termination.

| Header | Required | Description |
| --- | --- | --- |
| `Request-Id` | Yes | Active chat UUID. |
| `Length` | No | Body length if a close reason is included. |

The sender closes the application-level conversation after sending this message.

## 6. Communication Workflow

Normal flow:

```text
Initiator                                  Recipient
    |                                          |
    | UDP broadcast: DISCOVER                  |
    |----------------------------------------->|
    |                                          | nickname matches
    | TCP listen on advertised port            |
    |<-----------------------------------------| TCP connect
    |                                          |
    | TCP: HELLO with Request-Id               |
    |<-----------------------------------------|
    |                                          |
    | TCP: HANDSHAKE-RESPONSE ACCEPT           |
    |----------------------------------------->|
    |                                          |
    | TCP: TEXT                                |
    |----------------------------------------->|
    | TCP: TEXT response                       |
    |<-----------------------------------------|
    |                                          |
    | TCP: CLOSE                               |
    |----------------------------------------->|
```

Rejected handshake:

```text
Recipient sends HELLO
Initiator checks Request-Id and Deadline
Initiator sends HANDSHAKE-RESPONSE with Status: REJECT
Both sides stop the chat attempt
```

## 7. State Transitions

### Initiator

| State | Event | Next State |
| --- | --- | --- |
| `CreateRequest` | UUID and deadline generated | `BroadcastDiscovery` |
| `BroadcastDiscovery` | UDP message sent | `WaitForTcp` |
| `WaitForTcp` | TCP client connects before deadline | `ValidateHello` |
| `WaitForTcp` | Deadline expires | `Closed` |
| `ValidateHello` | Valid UUID and deadline | `ChatTurn` |
| `ValidateHello` | Invalid UUID or expired deadline | `Closed` |
| `ChatTurn` | TEXT sent and response received | `ChatTurn` |
| `ChatTurn` | CLOSE sent or received | `Closed` |

### Recipient

| State | Event | Next State |
| --- | --- | --- |
| `ListenDiscovery` | DISCOVER for other nickname | `ListenDiscovery` |
| `ListenDiscovery` | Matching DISCOVER before deadline | `AskUser` |
| `AskUser` | User declines | `ListenDiscovery` |
| `AskUser` | User accepts | `ConnectTcp` |
| `ConnectTcp` | TCP connected | `SendHello` |
| `SendHello` | ACCEPT received | `ReplyTurn` |
| `SendHello` | REJECT received | `Closed` |
| `ReplyTurn` | TEXT received and reply sent | `ReplyTurn` |
| `ReplyTurn` | CLOSE sent or received | `Closed` |

## 8. Error Handling

| Error | Handling |
| --- | --- |
| Malformed UDP discovery | Recipient ignores it and keeps listening. |
| Nickname mismatch | Recipient ignores the discovery message. |
| Expired discovery deadline | Recipient ignores it or Initiator rejects the handshake. |
| Invalid UUID in HELLO | Initiator sends `HANDSHAKE-RESPONSE` with `Status: REJECT`. |
| Unexpected TCP message type | Receiver treats it as a protocol error and closes. |
| Oversized header/body | Receiver rejects the frame to avoid unbounded memory use. |
| TCP disconnect | Application reports the closed connection and exits. |

## 9. Assumptions and Limitations

- Discovery is limited to the local broadcast domain.
- Network firewalls must allow the UDP discovery port and the chosen TCP port.
- The implementation supports one accepted chat session per application run.
- The chat is simplex turn-taking at the application level: the initiator sends a `TEXT`, then waits for exactly one `TEXT` or `CLOSE` response before sending again.
- Messages are not encrypted or authenticated beyond UUID validation.
- UDP discovery is not guaranteed to arrive. If no recipient responds before the deadline, the attempt fails.
