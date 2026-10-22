// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

namespace Jakar.SignalR.Chats;


public enum PresenceLogin : byte
{
    /// <summary> The user's first connection: they came online. </summary>
    FirstConnection,

    /// <summary> The user was already online on another connection. </summary>
    AnotherConnection,

    /// <summary> This connection was already logged in as this user. </summary>
    AlreadyLoggedIn,

    /// <summary> This connection is logged in as a different user (it must log out first). </summary>
    Conflict
}



/// <summary> What removing a connection changed. </summary>
/// <param name="User"> Who the connection was logged in as, or <see langword="null" /> when it wasn't. </param>
/// <param name="RoomsLeft"> The rooms the user is no longer in at all (none of their other connections are in them). </param>
/// <param name="WasLastConnection"> The user has no connections left. </param>
public readonly record struct PresenceExit( ChatUser? User, string[] RoomsLeft, bool WasLastConnection );



/// <summary>
///     Who is connected, as whom, and in which rooms. A user may have any number of connections (devices, tabs);
///     they are in a room while any of them is.
/// </summary>
/// <remarks>
///     <see cref="InMemoryChatPresence" /> is the default, for a single server. Behind a SignalR backplane (several
///     servers), register an implementation every server shares.
/// </remarks>
public interface IChatPresence
{
    ValueTask<PresenceLogin> LoginAsync( string connectionID, ChatUser user, CancellationToken token = default );

    /// <summary>
    ///     Logs the connection out (and out of its rooms). Used for <see cref="IChatHub.Logout" /> and when the
    ///     connection closes.
    /// </summary>
    ValueTask<PresenceExit> RemoveAsync( string connectionID, CancellationToken token = default );

    ValueTask<ChatUser?> GetUserAsync( string connectionID, CancellationToken token = default );

    /// <returns> <see langword="true" /> when the user wasn't in <paramref name="room" /> on any connection before. </returns>
    ValueTask<bool> JoinAsync( string connectionID, string room, CancellationToken token = default );

    /// <returns>
    ///     <see langword="true" /> when this connection was in <paramref name="room" /> and none of the user's
    ///     connections are any more.
    /// </returns>
    ValueTask<bool> LeaveAsync( string connectionID, string room, CancellationToken token = default );

    ValueTask<bool>       IsInRoomAsync( string connectionID, string            room, CancellationToken token = default );
    ValueTask<ChatUser[]> GetUsersAsync( string room,         CancellationToken token = default );
    ValueTask<string[]>   GetRoomsAsync( string connectionID, CancellationToken token = default );
}



/// <summary> <see cref="IChatPresence" /> in this process's memory. </summary>
public sealed class InMemoryChatPresence : IChatPresence
{
    private readonly Dictionary<string, Connection>                   __connections = new(StringComparer.Ordinal);
    private readonly Lock                                             __lock        = new();
    private readonly Dictionary<string, Dictionary<Guid, RoomMember>> __rooms       = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, int>                            __users       = [];


    public ValueTask<PresenceLogin> LoginAsync( string connectionID, ChatUser user, CancellationToken token = default )
    {
        lock ( __lock )
        {
            if ( __connections.TryGetValue(connectionID, out Connection? existing) )
            {
                return ValueTask.FromResult(existing.User.Equals(user)
                                                ? PresenceLogin.AlreadyLoggedIn
                                                : PresenceLogin.Conflict);
            }

            __connections[connectionID] = new Connection(user);
            int count = __users.GetValueOrDefault(user.UserID);
            __users[user.UserID] = count + 1;

            return ValueTask.FromResult(count == 0
                                            ? PresenceLogin.FirstConnection
                                            : PresenceLogin.AnotherConnection);
        }
    }

    public ValueTask<PresenceExit> RemoveAsync( string connectionID, CancellationToken token = default )
    {
        lock ( __lock )
        {
            if ( !__connections.Remove(connectionID, out Connection? connection) )
            {
                return ValueTask.FromResult(new PresenceExit(null, [], false));
            }

            List<string> left = [];

            foreach ( string room in connection.Rooms )
            {
                if ( RemoveFromRoom(room, connection.User) )
                {
                    left.Add(room);
                }
            }

            int  count = __users.GetValueOrDefault(connection.User.UserID) - 1;
            bool last  = count <= 0;

            if ( last )
            {
                __users.Remove(connection.User.UserID);
            }
            else
            {
                __users[connection.User.UserID] = count;
            }

            return ValueTask.FromResult(new PresenceExit(connection.User, [.. left], last));
        }
    }

    public ValueTask<ChatUser?> GetUserAsync( string connectionID, CancellationToken token = default )
    {
        lock ( __lock )
        {
            return ValueTask.FromResult(__connections.TryGetValue(connectionID, out Connection? connection)
                                            ? connection.User
                                            : null);
        }
    }

    public ValueTask<bool> JoinAsync( string connectionID, string room, CancellationToken token = default )
    {
        lock ( __lock )
        {
            if ( !__connections.TryGetValue(connectionID, out Connection? connection) )
            {
                throw new InvalidOperationException($"Connection '{connectionID}' isn't logged in.");
            }

            if ( !connection.Rooms.Add(room) )
            {
                return ValueTask.FromResult(false);
            }

            if ( !__rooms.TryGetValue(room, out var members) )
            {
                __rooms[room] = members = [];
            }

            if ( members.TryGetValue(connection.User.UserID, out RoomMember member) )
            {
                members[connection.User.UserID] = member with { Connections = member.Connections + 1 };
                return ValueTask.FromResult(false);
            }

            members[connection.User.UserID] = new RoomMember(connection.User, 1);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<bool> LeaveAsync( string connectionID, string room, CancellationToken token = default )
    {
        lock ( __lock )
        {
            if ( !__connections.TryGetValue(connectionID, out Connection? connection) || !connection.Rooms.Remove(room) )
            {
                return ValueTask.FromResult(false);
            }

            return ValueTask.FromResult(RemoveFromRoom(room, connection.User));
        }
    }

    public ValueTask<bool> IsInRoomAsync( string connectionID, string room, CancellationToken token = default )
    {
        lock ( __lock ) { return ValueTask.FromResult(__connections.TryGetValue(connectionID, out Connection? connection) && connection.Rooms.Contains(room)); }
    }

    public ValueTask<ChatUser[]> GetUsersAsync( string room, CancellationToken token = default )
    {
        lock ( __lock )
        {
            if ( !__rooms.TryGetValue(room, out var members) )
            {
                return ValueTask.FromResult<ChatUser[]>([]);
            }

            var users                                                      = new ChatUser[members.Count];
            int index                                                      = 0;
            foreach ( RoomMember member in members.Values )
            {
                users[index++] = member.User;
            }

            return ValueTask.FromResult(users);
        }
    }

    public ValueTask<string[]> GetRoomsAsync( string connectionID, CancellationToken token = default )
    {
        lock ( __lock )
        {
            return ValueTask.FromResult(__connections.TryGetValue(connectionID, out Connection? connection)
                                            ? [.. connection.Rooms]
                                            : Array.Empty<string>());
        }
    }


    // Callers hold __lock.
    private bool RemoveFromRoom( string room, ChatUser user )
    {
        if ( !__rooms.TryGetValue(room, out var members) || !members.TryGetValue(user.UserID, out RoomMember member) )
        {
            return false;
        }

        if ( member.Connections > 1 )
        {
            members[user.UserID] = member with { Connections = member.Connections - 1 };
            return false;
        }

        members.Remove(user.UserID);
        if ( members.Count == 0 )
        {
            __rooms.Remove(room);
        }

        return true;
    }



    private sealed class Connection( ChatUser user )
    {
        public HashSet<string> Rooms { get; } = new(StringComparer.Ordinal);
        public ChatUser        User  { get; } = user;
    }



    private readonly record struct RoomMember( ChatUser User, int Connections );
}
