// Jakar.SignalR.Chats.Tests
// 10/21/2026

using System.Text;
using Jakar.Extensions;
using Jakar.Extensions.UserGuid;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;



namespace Jakar.SignalR.Chats.Tests;


/// <summary> End to end: a Kestrel chat server and <see cref="ChatClient" />s over WebSockets. </summary>
[TestFixture]
public sealed class ChatTests
{
    [SetUp] public async Task SetUpAsync() { __server = await ChatServer.StartAsync(); }

    [TearDown] public async Task TearDownAsync()
    {
        foreach ( ChatClient client in __clients )
        {
            await client.DisposeAsync();
        }

        __clients.Clear();
        await __server.DisposeAsync();
    }

    private const    string           ROOM      = "general";
    private readonly List<ChatClient> __clients = [];
    private          ChatServer       __server  = null!;


    private async Task<ChatClient> ConnectAsync( string userName, params string[] rooms )
    {
        ChatClient client = __server.CreateClient();
        __clients.Add(client);
        await client.StartAsync(CancellationToken.None);
        await client.LoginAsync(new ChatUser(Guid.NewGuid(), userName, userName.ToUpperInvariant()));

        foreach ( string room in rooms )
        {
            await client.JoinRoomAsync(room);
        }

        return client;
    }


    [Test] public async Task Send_ReachesEveryoneInTheRoom_WithAttachments()
    {
        ChatClient alice = await ConnectAsync("alice", ROOM);
        ChatClient bob   = await ConnectAsync("bob",   ROOM);

        FileData       attachment = new(FileMetaData.Create("hello.txt", MimeType.Text), Encoding.UTF8.GetBytes("hello, world"));
        InstantMessage sent       = await alice.SendAsync(ROOM, "hi bob", [attachment]);

        ChatRoom bobs = bob.Rooms.Find(ROOM)!;
        await Wait.UntilAsync(() => bobs.Messages.Count == 1, "bob has the message");
        InstantMessage received = bobs.Messages.Get(0);

        Assert.Multiple(() =>
                        {
                            Assert.That(sent.Sequence,                       Is.EqualTo(1));
                            Assert.That(sent.ID,                             Is.Not.EqualTo(Guid.Empty));
                            Assert.That(sent.Sender,                         Is.EqualTo(alice.User));
                            Assert.That(received.ID,                         Is.EqualTo(sent.ID));
                            Assert.That(received.Text,                       Is.EqualTo("hi bob"));
                            Assert.That(received.Attachments,                Has.Length.EqualTo(1));
                            Assert.That(received.Attachments![0].Payload,    Is.EqualTo(attachment.Payload));
                            Assert.That(bobs.UnreadChats,                    Is.EqualTo(1));
                            Assert.That(alice.Rooms.Find(ROOM)!.Messages,    Has.Count.EqualTo(1), "the pending copy was replaced, not duplicated");
                            Assert.That(alice.Rooms.Find(ROOM)!.UnreadChats, Is.Zero,              "your own messages aren't unread");
                        });
    }


    [Test] public async Task Send_SameClientID_IsStoredAndBroadcastOnce()
    {
        ChatClient alice = await ConnectAsync("alice", ROOM);
        ChatClient bob   = await ConnectAsync("bob",   ROOM);

        int received = 0;
        bob.MessageReceived += ( _, _ ) => Interlocked.Increment(ref received);

        InstantMessage message = InstantMessage.Create(ROOM, "once");
        InstantMessage first   = await alice.SendAsync(message);
        InstantMessage again   = await alice.SendAsync(message);

        await Wait.UntilAsync(() => received >= 1, "bob has the message");
        await Task.Delay(200);

        Assert.Multiple(() =>
                        {
                            Assert.That(again.ID,                                                         Is.EqualTo(first.ID));
                            Assert.That(received,                                                         Is.EqualTo(1));
                            Assert.That(__server.Store.GetHistoryAsync(HistoryQuery.Latest(ROOM)).Result, Has.Length.EqualTo(1));
                        });
    }


    [Test] public async Task Presence_JoinLeaveLogout_UpdateTheRoom()
    {
        ChatClient         alice  = await ConnectAsync("alice", ROOM);
        ChatRoom           room   = alice.Rooms.Find(ROOM)!;
        List<HubEventType> events = [];

        alice.OnEvent += ( _, e ) =>
                         {
                             lock ( events ) { events.Add(e.Type); }
                         };

        ChatClient bob = await ConnectAsync("bob", ROOM);
        await Wait.UntilAsync(() => room.Users.Count == 2, "bob shows in alice's room");
        Assert.That(bob.Rooms.Find(ROOM)!.Users, Has.Count.EqualTo(2), "joining returns who is already there");

        await bob.LeaveRoomAsync(ROOM);
        await Wait.UntilAsync(() => room.Users.Count == 1, "bob left alice's room");

        await bob.JoinRoomAsync(ROOM);
        await Wait.UntilAsync(() => room.Users.Count == 2, "bob is back");

        await bob.LogoutAsync();
        await Wait.UntilAsync(() => room.Users.Count == 1, "bob logged out");

        await Wait.UntilAsync(() =>
                              {
                                  lock ( events ) { return events.Contains(HubEventType.Logout); }
                              },
                              "alice hears the logout");

        lock ( events ) { Assert.That(events, Is.SupersetOf(new[] { HubEventType.Login, HubEventType.JoinRoom, HubEventType.LeaveRoom, HubEventType.Logout })); }
    }


    [Test] public async Task Disconnection_IsAnnouncedToTheRoom()
    {
        ChatClient alice = await ConnectAsync("alice", ROOM);
        ChatClient bob   = await ConnectAsync("bob",   ROOM);
        ChatRoom   room  = alice.Rooms.Find(ROOM)!;
        await Wait.UntilAsync(() => room.Users.Count == 2, "bob shows in alice's room");

        HubEvent? disconnected = null;

        alice.OnEvent += ( _, e ) =>
                         {
                             if ( e.Type == HubEventType.Disconnection )
                             {
                                 disconnected = e;
                             }
                         };

        await bob.StopAsync(CancellationToken.None);

        await Wait.UntilAsync(() => disconnected is not null, "alice hears bob disconnect");

        Assert.Multiple(() =>
                        {
                            Assert.That(disconnected!.User, Is.EqualTo(bob.User));
                            Assert.That(room.Users,         Has.Count.EqualTo(1));
                        });
    }


    [Test] public async Task Typing_ShowsThenExpires()
    {
        ChatClient alice = await ConnectAsync("alice", ROOM);
        ChatClient bob   = await ConnectAsync("bob",   ROOM);
        ChatRoom   room  = bob.Rooms.Find(ROOM)!;

        await alice.TypingAsync(ROOM);
        await Wait.UntilAsync(() => room.TypingUsers.Count == 1, "bob sees alice typing");
        Assert.That(room.TypingUsers.Get(0), Is.EqualTo(alice.User));

        await Wait.UntilAsync(() => room.TypingUsers.Count == 0, "alice's typing expires");
    }


    [Test] public async Task History_JoinGetsTheLatest_AndOlderPagesBack()
    {
        ChatClient alice = await ConnectAsync("alice", ROOM);
        for ( int i = 1; i <= 120; i++ )
        {
            await alice.SendAsync(ROOM, $"message {i}");
        }

        ChatClient bob  = await ConnectAsync("bob", ROOM);
        ChatRoom   room = bob.Rooms.Find(ROOM)!;

        Assert.Multiple(() =>
                        {
                            Assert.That(room.Messages,                 Has.Count.EqualTo(HistoryQuery.DEFAULT_COUNT));
                            Assert.That(room.Messages.Get(0).Sequence, Is.EqualTo(71));
                            Assert.That(room.UnreadChats,              Is.Zero, "history isn't unread");
                        });

        var older = await bob.LoadOlderAsync(ROOM);

        Assert.Multiple(() =>
                        {
                            Assert.That(Array.ConvertAll(older, static m => m.Sequence), Is.EqualTo(Enumerable.Range(21, 50).Select(static i => (long)i)));
                            Assert.That(room.Messages,                                   Has.Count.EqualTo(100));
                            Assert.That(room.Messages.Get(0).Sequence,                   Is.EqualTo(21));
                        });

        await bob.LoadOlderAsync(ROOM);
        Assert.That(await bob.LoadOlderAsync(ROOM),                                    Is.Empty);
        Assert.That(Array.ConvertAll(room.Messages.ToArray(), static m => m.Sequence), Is.EqualTo(Enumerable.Range(1, 120).Select(static i => (long)i)));
    }


    [Test] public async Task ServerRestart_ReconnectsRejoinsCatchesUpAndDeliversTheOutbox()
    {
        ChatClient alice = await ConnectAsync("alice", ROOM);
        ChatClient bob   = await ConnectAsync("bob",   ROOM);
        await alice.SendAsync(ROOM, "before");

        ChatRoom bobs = bob.Rooms.Find(ROOM)!;
        await Wait.UntilAsync(() => bobs.Messages.Count == 1, "bob has the first message");

        await __server.StopAsync();
        await Wait.UntilAsync(() => !alice.IsReady && !bob.IsReady, "both clients notice");

        // Queued while the server is down.
        var queued = alice.SendAsync(ROOM, "while you were away");
        await Task.Delay(300);
        Assert.That(queued.IsCompleted, Is.False);

        await __server.StartAgainAsync();
        InstantMessage delivered = await queued.WaitAsync(Wait.Timeout);

        await Wait.UntilAsync(() => bobs.Messages.Count == 2, "bob gets the queued message");
        await Wait.UntilAsync(() => bobs.Users.Count    == 2, "the room knows both users again");

        Assert.Multiple(() =>
                        {
                            Assert.That(delivered.Sequence,               Is.EqualTo(2));
                            Assert.That(bobs.Messages.Get(1).ID,          Is.EqualTo(delivered.ID));
                            Assert.That(bobs.LastSequence,                Is.EqualTo(2));
                            Assert.That(alice.User,                       Is.Not.Null);
                            Assert.That(alice.Rooms.Find(ROOM)!.IsJoined, Is.True);
                        });

        // And the session works as before.
        await bob.SendAsync(ROOM, "welcome back");
        await Wait.UntilAsync(() => alice.Rooms.Find(ROOM)!.Messages.Count == 3, "alice gets bob's reply");
    }


    [Test] public async Task Server_RejectsWhatItShould()
    {
        ChatClient alice = await ConnectAsync("alice", ROOM);

        await Assert.ThrowsAsync<InvalidOperationException>(() => alice.SendAsync("elsewhere", "hello"), "not joined, caught by the client");

        HubException empty = ( await Assert.ThrowsAsync<HubException>(() => alice.SendAsync(ROOM, "  ")) )!;
        Assert.That(empty.Message,                    Does.Contain("empty"));
        Assert.That(alice.Rooms.Find(ROOM)!.Messages, Is.Empty, "a refused message doesn't stay pending");

        // A raw connection that never logged in.
        await using HubConnection raw = new HubConnectionBuilder().WithUrl(new Uri(__server.Address, IChatHub.PATH)).AddChatProtocol().Build();
        await raw.StartAsync();
        HubException notLoggedIn = ( await Assert.ThrowsAsync<HubException>(() => raw.InvokeAsync<ChatRoomState>(nameof(IChatHub.JoinRoom), ROOM)) )!;
        Assert.That(notLoggedIn.Message, Does.Contain("Log in first"));
    }
}
