# Jakar.SignalR

A strongly typed SignalR chat client and server. It supports any number of rooms and any number of users per room. You
get presence, typing, and messages with attachments that survive lost connections, plus history that catches up after a
reconnection.

| Package              | What it is                                                                                                                                                                      |
|----------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Jakar.SignalR.Chats.Shared` | The models (`ChatUser`, `InstantMessage`, `HubEvent`, `ChatRoomState`, `HistoryQuery`) and the two contracts: `IChatHub` (client → server) and `IChatClient` (server → client). |
| `Jakar.SignalR.Chats.Server` | `ChatHub : Hub<IChatClient>, IChatHub`, with presence (`IChatPresence`) and message storage (`IChatStore`).                                                                     |
| `Jakar.SignalR.Chats`        | `ChatClient`: implements `IChatHub`, handles `IChatClient`, and keeps observable `ChatRoom`s up to date.                                                                        |

## Server

```csharp
builder.Services.AddChatServer(options => options.AllowAnonymous = builder.Environment.IsDevelopment());
// Optional, before AddChatServer: your own store / presence (a database, a backplane-wide presence).
// builder.Services.AddSingleton<IChatStore, MyDatabaseChatStore>();

app.UseAuthentication();
app.MapChatHub(); // at IChatHub.PATH, "/Chat/hub"
```

- **Who users are:** on an authenticated connection, the user comes from the connection's claims (`NameIdentifier` or
  `sub` as a `Guid`, and the identity's name). Unauthenticated connections are refused unless `AllowAnonymous` is on.
- **Who may join what:** derive from `ChatHub`, override `CanJoinAsync(user, room)`, and register it with
  `AddChatServer<MyHub>()` and `MapChatHub<MyHub>()`.
- **Attachments:** `ChatServerOptions` limits their count (`MaxAttachments`) and total size (`MaxAttachmentsLength`).
  The hub's `MaximumReceiveMessageSize` is raised to fit them, because SignalR's 32 KiB default doesn't.
- **Several servers:** the default `InMemoryChatPresence` and `InMemoryChatStore` only work within one process. Behind a
  SignalR backplane, register implementations that every server shares.

## Client

```csharp
builder.Services.AddChatClient(options =>
{
    options.HostInfo            = new Uri("https://chat.example.com");
    options.AccessTokenProvider = () => tokens.GetAccessTokenAsync();
});

// later, from the ChatClient singleton
await chat.LoginAsync(new ChatUser(userID, "alice", "Alice Smith"));
ChatRoom room = await chat.JoinRoomAsync("general");   // users + latest messages

room.Messages      // InstantMessageCollection, in Sequence order (pending sends last)
room.Users         // who is in the room
room.TypingUsers   // who is typing right now
room.UnreadChats   // set room.IsActive = true while it's on screen

await chat.TypingAsync("general");                                   // throttled; call on every keystroke
InstantMessage sent = await chat.SendAsync("general", "hi!", [file]); // file: Jakar.Extensions.UserGuid.FileData
await chat.LoadOlderAsync("general");                                // page back through history
```

Events: `MessageReceived`, `OnEvent` (every `HubEvent`), `UserChanged`, `StateChanged`, `Ready`, `Error`, plus
`ChatRoom.MessageReceived` and the collections' change notifications. All of them fire on SignalR's threads, so a UI has
to marshal them to its own thread.

## Reliability

- **Connecting:** the client retries the first connection and every lost one for as long as it runs (`RetryDelays`,
  where the last delay repeats).
- **Reconnecting:** the server forgets a connection once it drops. On reconnection the client logs back in, rejoins its
  rooms through `Reconnection`, and fetches every message after each room's `LastSequence`.
- **Sending:** `SendAsync` shows the message as pending right away and completes once the server has stored it. While
  disconnected, messages wait in an outbox and are resent in order. Each message has a `ClientID`, so a resend the
  server already has returns the stored copy and isn't posted twice.
- **Ordering:** the store gives each message a per-room `Sequence`. That number orders the room and is the cursor for
  `HistoryQuery` (`Latest`, `Older(before)`, `Newer(after)`).
- **Persisting:** `ChatRoom` and the collections serialize with `[JsonModel]`. Restore a room into `Rooms`, and joining
  it again catches up from its `LastSequence`.

## Events

| `HubEventType`                   | When                                                               | Sent to                                        |
|----------------------------------|--------------------------------------------------------------------|------------------------------------------------|
| `Login` / `Logout`               | a user's first connection logs in / their last connection logs out | everyone (`BroadcastPresence`)                 |
| `JoinRoom` / `LeaveRoom`         | a user enters a room / is no longer in it on any connection        | the room                                       |
| `Disconnection` / `Reconnection` | a user's connection drops (not a logout) / comes back              | the room                                       |
| `Typing`                         | a user is typing                                                   | the room, apart from the user's own connection |
