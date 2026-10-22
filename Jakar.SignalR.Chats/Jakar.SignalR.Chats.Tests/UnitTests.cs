// Jakar.SignalR.Chats.Tests
// 10/21/2026

using Jakar.Extensions;
using Jakar.Extensions.UserGuid;



namespace Jakar.SignalR.Chats.Tests;


[TestFixture]
public sealed class StoreTests
{
    private static InstantMessage Message( string room, ChatUser sender, Guid? clientID = null ) =>
        new InstantMessage
        {
            ClientID = clientID ?? Guid.NewGuid(),
            Room     = room,
            Text     = "x"
        }.Stamp(Guid.NewGuid(), sender, DateTimeOffset.UtcNow);


    [Test] public async Task Add_SequencesPerRoom_AndDeduplicatesBySenderAndClientID()
    {
        InMemoryChatStore store  = new();
        ChatUser          alice  = new(Guid.NewGuid(), "alice", "Alice");
        ChatUser          bob    = new(Guid.NewGuid(), "bob", "Bob");
        Guid              shared = Guid.NewGuid();

        ( InstantMessage a1, bool added1 ) = await store.AddAsync(Message("a", alice, shared));
        ( InstantMessage a2, bool added2 ) = await store.AddAsync(Message("a", alice, shared));
        ( InstantMessage a3, bool added3 ) = await store.AddAsync(Message("a", bob,   shared));
        ( InstantMessage b1, _ )           = await store.AddAsync(Message("b", alice));

        Assert.Multiple(() =>
                        {
                            Assert.That(( a1.Sequence, added1 ), Is.EqualTo(( 1L, true )));
                            Assert.That(( a2.Sequence, added2 ), Is.EqualTo(( 1L, false )));
                            Assert.That(a2.ID,                   Is.EqualTo(a1.ID));
                            Assert.That(( a3.Sequence, added3 ), Is.EqualTo(( 2L, true )), "another sender's ClientID doesn't collide");
                            Assert.That(b1.Sequence,             Is.EqualTo(1));
                        });
    }


    [Test] public async Task History_PagesBothWays_AndTrims()
    {
        InMemoryChatStore store = new() { MaxMessagesPerRoom = 100 };
        ChatUser          alice = new(Guid.NewGuid(), "alice", "Alice");
        for ( int i = 0; i < 120; i++ )
        {
            await store.AddAsync(Message("a", alice));
        }

        long[] latest = sequences(await store.GetHistoryAsync(HistoryQuery.Latest("a", 10)));
        long[] older  = sequences(await store.GetHistoryAsync(HistoryQuery.Older("a", 111, 10)));
        long[] newer  = sequences(await store.GetHistoryAsync(HistoryQuery.Newer("a", 115, 10)));
        long[] oldest = sequences(await store.GetHistoryAsync(HistoryQuery.Older("a", 25, 10)));

        Assert.Multiple(() =>
                        {
                            Assert.That(latest, Is.EqualTo(range(111, 120)));
                            Assert.That(older,  Is.EqualTo(range(101, 110)));
                            Assert.That(newer,  Is.EqualTo(range(116, 120)));
                            Assert.That(oldest, Is.EqualTo(range(21,  24)), "the first 20 were trimmed");
                        });

        static long[] sequences( InstantMessage[] messages ) { return Array.ConvertAll(messages, static m => m.Sequence); }

        static long[] range( long from, long to ) { return Enumerable.Range((int)from, (int)( to - from + 1 )).Select(static i => (long)i).ToArray(); }
    }
}



[TestFixture]
public sealed class PresenceTests
{
    [Test] public async Task AUserIsInARoomWhileAnyOfTheirConnectionsIs()
    {
        InMemoryChatPresence presence = new();
        ChatUser             alice    = new(Guid.NewGuid(), "alice", "Alice");

        Assert.That(await presence.LoginAsync("phone",  alice),                                      Is.EqualTo(PresenceLogin.FirstConnection));
        Assert.That(await presence.LoginAsync("laptop", alice),                                      Is.EqualTo(PresenceLogin.AnotherConnection));
        Assert.That(await presence.LoginAsync("laptop", alice),                                      Is.EqualTo(PresenceLogin.AlreadyLoggedIn));
        Assert.That(await presence.LoginAsync("laptop", new ChatUser(Guid.NewGuid(), "eve", "Eve")), Is.EqualTo(PresenceLogin.Conflict));

        Assert.That(await presence.JoinAsync("phone",  "room"), Is.True);
        Assert.That(await presence.JoinAsync("laptop", "room"), Is.False, "already there on the phone");
        Assert.That(await presence.LeaveAsync("phone", "room"), Is.False, "still there on the laptop");
        Assert.That(await presence.GetUsersAsync("room"),       Is.EqualTo(new[] { alice }));

        PresenceExit phone  = await presence.RemoveAsync("phone");
        PresenceExit laptop = await presence.RemoveAsync("laptop");

        var remaining = await presence.GetUsersAsync("room");

        Assert.Multiple(() =>
                        {
                            Assert.That(phone.WasLastConnection,  Is.False);
                            Assert.That(laptop.WasLastConnection, Is.True);
                            Assert.That(laptop.RoomsLeft,         Is.EqualTo(new[] { "room" }));
                            Assert.That(remaining,                Is.Empty);
                        });
    }
}



[TestFixture]
public sealed class JsonTests
{
    [Test] public void ChatRoom_RoundTrips_WithItsMessages()
    {
        ChatUser alice = new(Guid.NewGuid(), "alice", "Alice");
        ChatRoom room  = new("general") { IsJoined = true };
        room.Apply(InstantMessage.Create("general", "hello").Stamp(Guid.NewGuid(), alice, DateTimeOffset.UtcNow, 1), null);
        room.Users.Add(alice);

        ChatRoom copy = ChatRoom.FromJson(room.ToJson());

        Assert.Multiple(() =>
                        {
                            Assert.That(copy.Name,                   Is.EqualTo("general"));
                            Assert.That(copy.IsJoined,               Is.True);
                            Assert.That(copy.LastSequence,           Is.EqualTo(1));
                            Assert.That(copy.Messages,               Has.Count.EqualTo(1));
                            Assert.That(copy.Messages.Get(0).Text,   Is.EqualTo("hello"));
                            Assert.That(copy.Messages.Get(0).Sender, Is.EqualTo(alice));
                            Assert.That(copy.Users,                  Is.Empty, "who is online isn't kept");
                        });
    }


    [Test] public void InstantMessage_RoundTrips_WithAttachments()
    {
        FileData       file    = new(FileMetaData.Create("a.bin", MimeType.Binary), 1, 2, 3);
        InstantMessage message = InstantMessage.Create("general", "see attached", file).Stamp(Guid.NewGuid(), new ChatUser(Guid.NewGuid(), "a", "A"), DateTimeOffset.UtcNow, 7);

        InstantMessage copy = InstantMessage.FromJson(message.ToJson());

        Assert.Multiple(() =>
                        {
                            Assert.That(copy,                                  Is.EqualTo(message));
                            Assert.That(copy.Sequence,                         Is.EqualTo(7));
                            Assert.That(copy.Attachments![0].Payload,          Is.EqualTo(file.Payload));
                            Assert.That(copy.Attachments[0].MetaData.FileName, Is.EqualTo("a.bin"));
                        });
    }
}
