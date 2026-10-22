// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

namespace Jakar.SignalR.Chats;


/// <summary>
///     The chat hub: a strongly typed <see cref="Hub{T}" /> of <see cref="IChatClient" /> that implements
///     <see cref="IChatHub" />.
///     Map it with <see cref="ChatServerExtensions.MapChatHub{THub}" /> after
///     <see cref="ChatServerExtensions.AddChatServer{THub}" />.
/// </summary>
/// <remarks>
///     Derive from it to decide who may join which room (<see cref="CanJoinAsync" />), or how a connection's user is
///     resolved (<see cref="AuthenticateAsync" />).
///     Who is where lives in <see cref="IChatPresence" />, messages in <see cref="IChatStore" />; the hub itself is
///     created per call, like every hub.
/// </remarks>
public class ChatHub( IChatPresence presence, IChatStore store, IOptions<ChatServerOptions> options, TimeProvider clock ) : Hub<IChatClient>, IChatHub
{
    protected TimeProvider      _Clock         { get; } = clock;
    protected ChatServerOptions _Options       { get; } = options.Value;
    protected IChatPresence     _Presence      { get; } = presence;
    protected IChatStore        _Store         { get; } = store;
    protected string            _ConnectionID => Context.ConnectionId;
    protected CancellationToken _Aborted      => Context.ConnectionAborted;


    public virtual async Task<ChatUser> Login( ChatUser user )
    {
        ChatUser resolved = await ResolveUserAsync(user).ConfigureAwait(false);
        if ( await LoginConnectionAsync(resolved).ConfigureAwait(false) is PresenceLogin.FirstConnection && _Options.BroadcastPresence ) { await Clients.Others.LoggedIn(CreateEvent(HubEventType.Login, EMPTY, resolved)).ConfigureAwait(false); }

        return resolved;
    }


    public virtual async Task Logout()
    {
        string[]     rooms = await _Presence.GetRoomsAsync(_ConnectionID, _Aborted).ConfigureAwait(false);
        PresenceExit exit  = await _Presence.RemoveAsync(_ConnectionID, _Aborted).ConfigureAwait(false);
        if ( exit.User is null ) { return; }

        foreach ( string room in rooms ) { await Groups.RemoveFromGroupAsync(_ConnectionID, room, _Aborted).ConfigureAwait(false); }

        foreach ( string room in exit.RoomsLeft ) { await Clients.Group(room).LeftRoom(CreateEvent(HubEventType.LeaveRoom, room, exit.User)).ConfigureAwait(false); }

        if ( exit.WasLastConnection && _Options.BroadcastPresence ) { await Clients.Others.LoggedOut(CreateEvent(HubEventType.Logout, EMPTY, exit.User)).ConfigureAwait(false); }
    }


    public virtual async Task<ChatRoomState> JoinRoom( string room )
    {
        ChatUser user = await RequireUserAsync().ConfigureAwait(false);
        ValidateRoom(room);

        if ( !await CanJoinAsync(user, room).ConfigureAwait(false) ) { throw new HubException($"You may not join '{room}'."); }

        // The group first, so nothing posted between the two steps is missed.
        await Groups.AddToGroupAsync(_ConnectionID, room, _Aborted).ConfigureAwait(false);

        if ( await _Presence.JoinAsync(_ConnectionID, room, _Aborted).ConfigureAwait(false) ) { await Clients.OthersInGroup(room).JoinedRoom(CreateEvent(HubEventType.JoinRoom, room, user)).ConfigureAwait(false); }

        return new ChatRoomState
               {
                   Room     = room,
                   Users    = await _Presence.GetUsersAsync(room, _Aborted).ConfigureAwait(false),
                   Messages = await _Store.GetHistoryAsync(HistoryQuery.Latest(room, Math.Min(_Options.JoinHistoryCount, _Options.MaxHistoryCount)), _Aborted).ConfigureAwait(false)
               };
    }


    public virtual async Task LeaveRoom( string room )
    {
        ChatUser user = await RequireUserAsync().ConfigureAwait(false);
        ValidateRoom(room);

        bool left = await _Presence.LeaveAsync(_ConnectionID, room, _Aborted).ConfigureAwait(false);
        await Groups.RemoveFromGroupAsync(_ConnectionID, room, _Aborted).ConfigureAwait(false);

        if ( left ) { await Clients.Group(room).LeftRoom(CreateEvent(HubEventType.LeaveRoom, room, user)).ConfigureAwait(false); }
    }


    public virtual async Task Typing( string room )
    {
        ChatUser user = await RequireRoomAsync(room).ConfigureAwait(false);
        await Clients.OthersInGroup(room).Typing(CreateEvent(HubEventType.Typing, room, user)).ConfigureAwait(false);
    }


    public virtual async Task<InstantMessage> Send( InstantMessage message )
    {
        if ( message is null ) { throw new HubException("The message is missing."); }

        ChatUser user = await RequireRoomAsync(message.Room).ConfigureAwait(false);
        ValidateMessage(message);

        InstantMessage stamped = message.Stamp(Guid.CreateVersion7(), user, _Clock.GetUtcNow());
        ( InstantMessage stored, bool added ) = await _Store.AddAsync(stamped, _Aborted).ConfigureAwait(false);

        // A retry of a message that's already stored was broadcast the first time; whoever missed it catches up through History.
        if ( added ) { await Clients.Group(stored.Room).Received(stored).ConfigureAwait(false); }

        return stored;
    }


    public virtual async Task<InstantMessage[]> History( HistoryQuery query )
    {
        if ( query is null ) { throw new HubException("The query is missing."); }

        await RequireRoomAsync(query.Room).ConfigureAwait(false);
        int count = Math.Clamp(query.Count, 1, _Options.MaxHistoryCount);

        return await _Store.GetHistoryAsync(query with { Count = count }, _Aborted).ConfigureAwait(false);
    }


    public virtual async Task<ChatRoomState[]> Reconnection( ChatUser user, string[] rooms )
    {
        ChatUser resolved = await ResolveUserAsync(user).ConfigureAwait(false);

        // A lost connection wasn't a logout (no LoggedOut was sent), so coming back isn't a login either: no LoggedIn.
        await LoginConnectionAsync(resolved).ConfigureAwait(false);

        List<ChatRoomState> states = [];

        foreach ( string room in ( rooms ?? [] ).Distinct(StringComparer.Ordinal) )
        {
            ValidateRoom(room);
            if ( !await CanJoinAsync(resolved, room).ConfigureAwait(false) ) { continue; }

            await Groups.AddToGroupAsync(_ConnectionID, room, _Aborted).ConfigureAwait(false);

            if ( await _Presence.JoinAsync(_ConnectionID, room, _Aborted).ConfigureAwait(false) ) { await Clients.OthersInGroup(room).Reconnected(CreateEvent(HubEventType.Reconnection, room, resolved)).ConfigureAwait(false); }

            states.Add(new ChatRoomState
                       {
                           Room  = room,
                           Users = await _Presence.GetUsersAsync(room, _Aborted).ConfigureAwait(false)
                       });
        }

        return [.. states];
    }


    public override async Task OnDisconnectedAsync( Exception? exception )
    {
        // ConnectionAborted is already canceled here.
        PresenceExit exit = await _Presence.RemoveAsync(_ConnectionID, CancellationToken.None).ConfigureAwait(false);

        if ( exit.User is not null )
        {
            foreach ( string room in exit.RoomsLeft ) { await Clients.Group(room).Disconnected(CreateEvent(HubEventType.Disconnection, room, exit.User)).ConfigureAwait(false); }
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }


    /// <summary> Whether <paramref name="user" /> may join <paramref name="room" />. Everyone may, by default. </summary>
    protected virtual ValueTask<bool> CanJoinAsync( ChatUser user, string room ) => ValueTask.FromResult(true);


    /// <summary>
    ///     Who this connection is. An authenticated connection is the user its claims name: the id from
    ///     <see cref="ClaimTypes.NameIdentifier" /> (or <c>sub</c>) as a <see cref="Guid" />,
    ///     the user name from the identity's name; only <see cref="ChatUser.FullName" /> comes from
    ///     <paramref name="requested" />. Otherwise <paramref name="requested" /> itself,
    ///     when <see cref="ChatServerOptions.AllowAnonymous" /> allows it.
    /// </summary>
    protected virtual ValueTask<ChatUser> AuthenticateAsync( ChatUser requested )
    {
        ClaimsPrincipal? principal = Context.User;

        if ( principal?.Identity?.IsAuthenticated is true )
        {
            string? id = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub") ?? Context.UserIdentifier;
            if ( !Guid.TryParse(id, out Guid userID) ) { throw new HubException("The authenticated user has no Guid identifier claim."); }

            string userName = principal.Identity.Name ?? requested.UserName;

            string fullName = string.IsNullOrWhiteSpace(requested.FullName)
                                  ? userName
                                  : requested.FullName;

            return ValueTask.FromResult(new ChatUser(userID, userName, fullName));
        }

        if ( !_Options.AllowAnonymous ) { throw new HubException("Unauthorized: connect with an authenticated user."); }

        if ( requested.UserID == Guid.Empty ) { throw new HubException("The user has no UserID."); }

        return ValueTask.FromResult(requested);
    }


    protected HubEvent CreateEvent( HubEventType type, string room, ChatUser user ) => new(type, room, user, _Clock.GetUtcNow());


    protected async Task<ChatUser> RequireUserAsync() => await _Presence.GetUserAsync(_ConnectionID, _Aborted).ConfigureAwait(false) ?? throw new HubException("Log in first.");

    protected async Task<ChatUser> RequireRoomAsync( string room )
    {
        ChatUser user = await RequireUserAsync().ConfigureAwait(false);
        ValidateRoom(room);

        return await _Presence.IsInRoomAsync(_ConnectionID, room, _Aborted).ConfigureAwait(false)
                   ? user
                   : throw new HubException($"Join '{room}' first.");
    }


    protected virtual void ValidateRoom( [NotNull] string? room )
    {
        if ( string.IsNullOrWhiteSpace(room) ) { throw new HubException("The room has no name."); }

        if ( room.Length > _Options.MaxRoomNameLength ) { throw new HubException($"Room names are at most {_Options.MaxRoomNameLength} characters."); }
    }

    protected virtual void ValidateMessage( InstantMessage message )
    {
        if ( message.ClientID == Guid.Empty ) { throw new HubException("The message has no ClientID."); }

        string text           = message.Text                ?? EMPTY;
        int    attachments    = message.Attachments?.Length ?? 0;
        bool   hasAttachments = attachments > 0;

        if ( string.IsNullOrWhiteSpace(text) && !hasAttachments ) { throw new HubException("The message is empty."); }

        if ( text.Length > _Options.MaxTextLength ) { throw new HubException($"Messages are at most {_Options.MaxTextLength} characters."); }

        if ( attachments > _Options.MaxAttachments ) { throw new HubException($"Messages have at most {_Options.MaxAttachments} attachments."); }

        if ( hasAttachments && message.Attachments!.Any(static file => file is null || string.IsNullOrEmpty(file.Payload)) ) { throw new HubException("An attachment is empty."); }

        if ( message.GetAttachmentsLength() > _Options.MaxAttachmentsLength ) { throw new HubException($"Attachments are at most {_Options.MaxAttachmentsLength} characters together."); }
    }


    private async Task<ChatUser> ResolveUserAsync( ChatUser? requested )
    {
        if ( requested is null ) { throw new HubException("The user is missing."); }

        ChatUser user = await AuthenticateAsync(requested).ConfigureAwait(false);

        if ( user.UserName?.Length > USER_NAME || user.FullName?.Length > FULL_NAME ) { throw new HubException("The user's name is too long."); }

        return user;
    }

    private async Task<PresenceLogin> LoginConnectionAsync( ChatUser user )
    {
        PresenceLogin result = await _Presence.LoginAsync(_ConnectionID, user, _Aborted).ConfigureAwait(false);

        return result == PresenceLogin.Conflict
                   ? throw new HubException("This connection is logged in as another user: log out first.")
                   : result;
    }
}
