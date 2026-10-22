// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 09/25/2025  10:20

namespace Jakar.SignalR.Chats;


/// <summary>
///     The hub methods a chat client calls on the server. The server's <c>ChatHub</c> implements it, and so does the
///     client's <c>ChatClient</c>, so both sides are checked by the compiler.
/// </summary>
/// <remarks>
///     Failures the caller can act on (not logged in, not in the room, message too large, ...) are thrown as
///     <c>HubException</c>s, whose message reaches the client.
/// </remarks>
public interface IChatHub
{
    /// <summary> The default URL path the hub is mapped to. </summary>
    public const string PATH = "/Chat/hub";


    /// <summary>
    ///     Logs this connection in. The server decides who the user is (from the connection's authenticated claims when
    ///     it has them), and returns that user.
    /// </summary>
    /// <remarks>
    ///     The first connection of a user raises <see cref="IChatClient.LoggedIn" /> on everyone else. Logging in again
    ///     as the same user does nothing.
    /// </remarks>
    Task<ChatUser> Login( ChatUser user );

    /// <summary>
    ///     Leaves every room this connection is in and logs it out. The user's last connection raises
    ///     <see cref="IChatClient.LoggedOut" /> on everyone else.
    /// </summary>
    Task Logout();

    /// <summary>
    ///     Joins <paramref name="room" /> and returns who is in it and its latest messages. Raises
    ///     <see cref="IChatClient.JoinedRoom" /> on the room the first time the user joins it.
    /// </summary>
    Task<ChatRoomState> JoinRoom( string room );

    /// <summary>
    ///     Leaves <paramref name="room" />. Raises <see cref="IChatClient.LeftRoom" /> on the room once none of the
    ///     user's connections are in it.
    /// </summary>
    Task LeaveRoom( string room );

    /// <summary> Tells the rest of <paramref name="room" /> that the user is typing (<see cref="IChatClient.Typing" />). </summary>
    Task Typing( string room );

    /// <summary>
    ///     Posts <paramref name="message" /> to its <see cref="InstantMessage.Room" />, and returns it as stored. Every
    ///     connection in the room, the sender's included, gets it through <see cref="IChatClient.Received" />.
    /// </summary>
    /// <remarks>
    ///     Idempotent by <see cref="InstantMessage.ClientID" />: sending the same message again (a retry after a lost
    ///     connection) returns the stored copy and isn't broadcast twice.
    /// </remarks>
    Task<InstantMessage> Send( InstantMessage message );

    /// <summary> A page of a room's messages (see <see cref="HistoryQuery" />). The connection must be in the room. </summary>
    Task<InstantMessage[]> History( HistoryQuery query );

    /// <summary>
    ///     Restores a connection after it was lost: logs <paramref name="user" /> in and rejoins
    ///     <paramref name="rooms" />, raising <see cref="IChatClient.Reconnected" /> instead of
    ///     <see cref="IChatClient.JoinedRoom" />.
    /// </summary>
    /// <returns>
    ///     Each room's users (no messages: catch up with <see cref="History" /> after the last
    ///     <see cref="InstantMessage.Sequence" /> you have). Rooms the user may no longer join are left out.
    /// </returns>
    Task<ChatRoomState[]> Reconnection( ChatUser user, string[] rooms );
}



/// <summary>
///     The methods the server calls on chat clients. The server's hub is a <c>Hub&lt;IChatClient&gt;</c>; the client
///     registers a handler for each one.
/// </summary>
public interface IChatClient
{
    /// <summary> A message was posted to a room this connection is in. </summary>
    Task Received( InstantMessage message );

    /// <summary> <see cref="HubEventType.Typing" />: someone else in the room is typing. </summary>
    Task Typing( HubEvent value );

    /// <summary> <see cref="HubEventType.JoinRoom" />. </summary>
    Task JoinedRoom( HubEvent value );

    /// <summary> <see cref="HubEventType.LeaveRoom" />. </summary>
    Task LeftRoom( HubEvent value );

    /// <summary> <see cref="HubEventType.Login" />. </summary>
    Task LoggedIn( HubEvent value );

    /// <summary> <see cref="HubEventType.Logout" />. </summary>
    Task LoggedOut( HubEvent value );

    /// <summary> <see cref="HubEventType.Disconnection" />. </summary>
    Task Disconnected( HubEvent value );

    /// <summary> <see cref="HubEventType.Reconnection" />. </summary>
    Task Reconnected( HubEvent value );
}
