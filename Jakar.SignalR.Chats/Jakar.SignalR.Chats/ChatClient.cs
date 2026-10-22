// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 05/21/2025  16:51

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;



namespace Jakar.SignalR.Chats;


/// <summary>
///     A chat client that stays connected: the strongly typed client side of <see cref="IChatHub" /> and
///     <see cref="IChatClient" />, keeping <see cref="Rooms" /> up to date.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             It's a <see cref="BackgroundService" />: as a hosted service (
///             <see cref="ChatClientExtensions.AddChatClient" />) the host starts it, or call
///             <see cref="BackgroundService.StartAsync" /> yourself. It retries the first connection, and every lost one,
///             for as long as it runs (<see cref="ChatClientOptions.RetryDelays" />).
///         </item>
///         <item>
///             The user (<see cref="LoginAsync" />) and the joined rooms (<see cref="JoinRoomAsync" />) are remembered:
///             after a reconnection it logs back in, rejoins them, and fetches whatever was posted while it was away.
///         </item>
///         <item>
///             <see cref="SendAsync(string, string, IEnumerable{FileData}?, CancellationToken)" /> queues the message
///             and completes once the server has stored it, resending across lost connections; the server ignores
///             duplicates (<see cref="InstantMessage.ClientID" />).
///         </item>
///         <item> Events and collection changes come from SignalR's threads. </item>
///     </list>
/// </remarks>
public class ChatClient : BackgroundService, IChatHub, IAsyncDisposable
{
    private readonly SemaphoreSlim                       __flush      = new(1, 1);
    private readonly Disposables                         __handlers   = [];
    private readonly Lock                                __lock       = new();
    private readonly ConcurrentDictionary<Guid, Pending> __outbox     = new();
    private readonly SemaphoreSlim                       __sync       = new(1, 1);
    private readonly ConcurrentDictionary<string, long>  __typingSent = new(StringComparer.Ordinal);
    private          TaskCompletionSource<Exception?>    __closed     = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private          bool                                __disposed;
    private          long                                __order;
    private          TaskCompletionSource                __ready = NewReady();
    private          ChatUser?                           __requested;


    public ChatClient( IOptions<ChatClientOptions> options, ILogger<ChatClient>? logger = null ) : this(options.Value, logger) { }

    public ChatClient( ChatClientOptions options, ILogger? logger = null )
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        _Logger  = logger ?? NullLogger.Instance;

        IHubConnectionBuilder builder = new HubConnectionBuilder().WithUrl(options.GetHubUri(),
                                                                           http =>
                                                                           {
                                                                               if ( options.AccessTokenProvider is not null )
                                                                               {
                                                                                   http.AccessTokenProvider = options.AccessTokenProvider;
                                                                               }

                                                                               options.ConfigureHttpConnection?.Invoke(http);
                                                                           })
                                                                  .WithAutomaticReconnect(new RetryPolicy(options))
                                                                  .AddChatProtocol();

        options.ConfigureConnection?.Invoke(builder);
        Connection = builder.Build();

        Connection.Reconnecting += OnReconnectingAsync;
        Connection.Reconnected  += OnReconnectedAsync;
        Connection.Closed       += OnClosedAsync;

        IChatClient callbacks = new Callbacks(this);
        __handlers.Add(Connection.On<InstantMessage>(nameof(IChatClient.Received), callbacks.Received));
        __handlers.Add(Connection.On<HubEvent>(nameof(IChatClient.Typing),       callbacks.Typing));
        __handlers.Add(Connection.On<HubEvent>(nameof(IChatClient.JoinedRoom),   callbacks.JoinedRoom));
        __handlers.Add(Connection.On<HubEvent>(nameof(IChatClient.LeftRoom),     callbacks.LeftRoom));
        __handlers.Add(Connection.On<HubEvent>(nameof(IChatClient.LoggedIn),     callbacks.LoggedIn));
        __handlers.Add(Connection.On<HubEvent>(nameof(IChatClient.LoggedOut),    callbacks.LoggedOut));
        __handlers.Add(Connection.On<HubEvent>(nameof(IChatClient.Disconnected), callbacks.Disconnected));
        __handlers.Add(Connection.On<HubEvent>(nameof(IChatClient.Reconnected),  callbacks.Reconnected));
    }


    public    HubConnection      Connection { get; }
    public    bool               IsReady    => __ready.Task.IsCompletedSuccessfully;
    protected ILogger            _Logger     { get; }
    public    ChatClientOptions  Options    { get; }
    public    ChatRoomCollection Rooms      { get; } = new();
    public    HubConnectionState State      => Connection.State;

    /// <summary> Unread messages in every room. </summary>
    public long UnreadChats
    {
        get
        {
            long count                                         = 0;
            foreach ( ChatRoom room in Rooms.ToArray() )
            {
                count += room.UnreadChats;
            }

            return count;
        }
    }

    /// <summary> Who the server says this client is logged in as, or <see langword="null" />. </summary>
    public ChatUser? User { get; private set; }

    public async ValueTask DisposeAsync()
    {
        if ( __disposed )
        {
            return;
        }

        __disposed = true;
        base.Dispose();
        __handlers.Dispose();
        FailOutbox(new ObjectDisposedException(nameof(ChatClient)));
        await Connection.DisposeAsync().ConfigureAwait(false);
        __flush.Dispose();
        __sync.Dispose();
        GC.SuppressFinalize(this);
    }


    // ─── IChatHub ────────────────────────────────────────────────────────────

    Task<ChatUser> IChatHub.Login( ChatUser user ) => LoginAsync(user);

    Task IChatHub.Logout() => LogoutAsync();

    Task<ChatRoomState> IChatHub.JoinRoom( string room ) => JoinAsync(GetOrAddRoom(room), CancellationToken.None);

    Task IChatHub.LeaveRoom( string room ) => LeaveRoomAsync(room);

    Task IChatHub.Typing( string room ) => TypingAsync(room);

    Task<InstantMessage> IChatHub.Send( InstantMessage message ) => SendAsync(message);

    Task<InstantMessage[]> IChatHub.History( HistoryQuery query ) => HistoryAsync(query);

    Task<ChatRoomState[]> IChatHub.Reconnection( ChatUser user, string[] rooms ) => Connection.InvokeAsync<ChatRoomState[]>(nameof(IChatHub.Reconnection), user, rooms);


    /// <summary> Something failed in the background (logging back in, rejoining a room). The client keeps going. </summary>
    public event EventHandler<Exception>? Error;

    /// <summary>
    ///     A message arrived that the rooms didn't have (live, or caught up after a reconnection; not history paged in
    ///     with <see cref="LoadOlderAsync" />).
    /// </summary>
    public event EventHandler<InstantMessage>? MessageReceived;

    /// <summary> Every <see cref="HubEvent" /> from the server, after <see cref="Rooms" /> reflect it. </summary>
    public event EventHandler<HubEvent>? OnEvent;

    /// <summary> Connected, and logged back in and rejoined after a reconnection: calls go through now. </summary>
    public event EventHandler? Ready;

    public event EventHandler<HubConnectionState>? StateChanged;
    public event EventHandler<ChatUser?>?          UserChanged;


    public override void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }


    // ─── Connection ──────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync( CancellationToken token )
    {
        long attempt = 0;

        while ( !token.IsCancellationRequested )
        {
            var closed = __closed = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                await Connection.StartAsync(token).ConfigureAwait(false);
                attempt = 0;
                OnStateChanged();
                await SynchronizeSafeAsync(token).ConfigureAwait(false);

                // Automatic reconnection handles lost connections; this only returns once it gives up (or the server closes the connection for good).
                Exception? error = await closed.Task.WaitAsync(token).ConfigureAwait(false);
                _Logger.LogWarning(error, "The chat connection closed; connecting again.");
            }
            catch ( OperationCanceledException ) when ( token.IsCancellationRequested ) { break; }
            catch ( Exception e ) { _Logger.LogWarning(e, "Couldn't connect to the chat at {Uri}.", Options.GetHubUri()); }

            try { await Task.Delay(Options.GetRetryDelay(attempt++), Options.Clock, token).ConfigureAwait(false); }
            catch ( OperationCanceledException ) { break; }
        }
    }

    public override async Task StopAsync( CancellationToken token )
    {
        await base.StopAsync(token).ConfigureAwait(false);
        await Connection.StopAsync(token).ConfigureAwait(false);
    }


    private Task OnReconnectingAsync( Exception? error )
    {
        _Logger.LogInformation(error, "The chat connection was lost; reconnecting.");
        ResetReady();
        OnStateChanged();
        return Task.CompletedTask;
    }

    private Task OnReconnectedAsync( string? connectionID )
    {
        OnStateChanged();

        // Not awaited: SignalR runs this handler as part of reconnecting.
        _ = Task.Run(() => SynchronizeSafeAsync(CancellationToken.None));
        return Task.CompletedTask;
    }

    private Task OnClosedAsync( Exception? error )
    {
        ResetReady();
        OnStateChanged();
        __closed.TrySetResult(error);
        return Task.CompletedTask;
    }

    protected virtual void OnStateChanged() { StateChanged?.Invoke(this, Connection.State); }


    /// <summary>
    ///     Restores what the server forgot with the old connection: logs back in, rejoins the rooms, catches up on their
    ///     messages, and resends the outbox.
    /// </summary>
    private async Task SynchronizeSafeAsync( CancellationToken token )
    {
        try { await SynchronizeAsync(token).ConfigureAwait(false); }
        catch ( OperationCanceledException ) when ( token.IsCancellationRequested ) { }
        catch ( Exception e )
        {
            _Logger.LogWarning(e, "Couldn't restore the chat session.");
            OnError(e);
        }
    }

    private async Task SynchronizeAsync( CancellationToken token )
    {
        await __sync.WaitAsync(token).ConfigureAwait(false);

        try
        {
            ChatUser? requested = __requested;
            if ( requested is null )
            {
                return;
            }

            var joined = GetRooms(true);

            if ( User is not null && joined.Length > 0 )
            {
                string[] names  = Array.ConvertAll(joined, static room => room.Name);
                var      states = await Connection.InvokeAsync<ChatRoomState[]>(nameof(IChatHub.Reconnection), requested, names, token).ConfigureAwait(false);

                foreach ( ChatRoom room in joined )
                {
                    ChatRoomState? state = Array.Find(states, state => string.Equals(state.Room, room.Name, StringComparison.Ordinal));

                    if ( state is null )
                    {
                        // The server won't let us back in.
                        room.IsJoined = false;
                        room.ClearUsers();
                        continue;
                    }

                    room.SetUsers(state.Users);
                    await CatchUpAsync(room, room.LastSequence, token).ConfigureAwait(false);
                }

                return;
            }

            await LoginCoreAsync(requested, token).ConfigureAwait(false);

            foreach ( ChatRoom room in joined )
            {
                try { await JoinCoreAsync(room, token).ConfigureAwait(false); }
                catch ( HubException e )
                {
                    room.IsJoined = false;
                    _Logger.LogWarning(e, "Couldn't rejoin {Room}.", room.Name);
                    OnError(e);
                }
            }
        }
        finally
        {
            __sync.Release();
            SetReady();
            _ = FlushAsync();
        }
    }


    private static TaskCompletionSource NewReady() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void SetReady()
    {
        lock ( __lock )
        {
            if ( Connection.State != HubConnectionState.Connected || !__ready.TrySetResult() )
            {
                return;
            }
        }

        Ready?.Invoke(this, EventArgs.Empty);
    }

    private void ResetReady()
    {
        lock ( __lock )
        {
            if ( __ready.Task.IsCompleted )
            {
                __ready = NewReady();
            }
        }
    }

    /// <summary> Waits until the connection is up and restored (<see cref="Ready" />). </summary>
    public Task WaitUntilReadyAsync( CancellationToken token = default )
    {
        ObjectDisposedException.ThrowIf(__disposed, this);
        Task ready;
        lock ( __lock ) { ready = __ready.Task; }

        return ready.WaitAsync(token);
    }


    // ─── Users ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Logs in as <paramref name="user" /> (waiting for the connection), and stays logged in across reconnections.
    ///     Returns the user as the server resolved it.
    /// </summary>
    public async Task<ChatUser> LoginAsync( ChatUser user, CancellationToken token = default )
    {
        ArgumentNullException.ThrowIfNull(user);

        if ( User is not null && !User.Equals(user) )
        {
            await LogoutAsync(token).ConfigureAwait(false);
        }

        __requested = user;
        await WaitUntilReadyAsync(token).ConfigureAwait(false);
        await __sync.WaitAsync(token).ConfigureAwait(false);

        try { return await LoginCoreAsync(user, token).ConfigureAwait(false); }
        finally { __sync.Release(); }
    }

    private async Task<ChatUser> LoginCoreAsync( ChatUser user, CancellationToken token )
    {
        try
        {
            ChatUser resolved = await Connection.InvokeAsync<ChatUser>(nameof(IChatHub.Login), user, token).ConfigureAwait(false);
            SetUser(resolved);
            return resolved;
        }
        catch ( HubException )
        {
            // Refused (not a lost connection): don't keep trying.
            __requested = null;
            SetUser(null);
            throw;
        }
    }


    /// <summary> Leaves every room and logs out. Messages still waiting to be sent fail. </summary>
    public async Task LogoutAsync( CancellationToken token = default )
    {
        __requested = null;
        SetUser(null);

        foreach ( ChatRoom room in GetRooms(false) )
        {
            room.IsJoined = false;
            room.ClearUsers();
        }

        FailOutbox(new InvalidOperationException("Logged out."));
        if ( !IsReady )
        {
            return;
        }

        await __sync.WaitAsync(token).ConfigureAwait(false);

        try { await Connection.InvokeAsync(nameof(IChatHub.Logout), token).ConfigureAwait(false); }
        finally { __sync.Release(); }
    }

    private void SetUser( ChatUser? user )
    {
        if ( ReferenceEquals(User, user) )
        {
            return;
        }

        User = user;
        UserChanged?.Invoke(this, user);
    }


    // ─── Rooms ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Joins <paramref name="name" /> (waiting for the connection) and loads who is in it and its latest messages.
    ///     The room is rejoined after every reconnection until <see cref="LeaveRoomAsync" />.
    /// </summary>
    public async Task<ChatRoom> JoinRoomAsync( string name, CancellationToken token = default )
    {
        ChatRoom room = GetOrAddRoom(name);
        await JoinAsync(room, token).ConfigureAwait(false);
        return room;
    }

    public async Task JoinRoomsAsync( IEnumerable<string> names, CancellationToken token = default )
    {
        foreach ( string name in names )
        {
            await JoinRoomAsync(name, token).ConfigureAwait(false);
        }
    }

    private async Task<ChatRoomState> JoinAsync( ChatRoom room, CancellationToken token )
    {
        room.IsJoined = true;
        await WaitUntilReadyAsync(token).ConfigureAwait(false);
        await __sync.WaitAsync(token).ConfigureAwait(false);

        try { return await JoinCoreAsync(room, token).ConfigureAwait(false); }
        catch ( HubException )
        {
            room.IsJoined = false;
            throw;
        }
        finally { __sync.Release(); }
    }

    private async Task<ChatRoomState> JoinCoreAsync( ChatRoom room, CancellationToken token )
    {
        long          previous = room.LastSequence;
        ChatRoomState state    = await Connection.InvokeAsync<ChatRoomState>(nameof(IChatHub.JoinRoom), room.Name, token).ConfigureAwait(false);

        room.SetUsers(state.Users);
        foreach ( InstantMessage message in state.Messages )
        {
            Deliver(room, message, previous > 0);
        }

        // Seen before (rejoining, or restored from JSON): everything since then is new to us. Otherwise the latest page is just history.
        if ( previous > 0 )
        {
            await CatchUpAsync(room, previous, token).ConfigureAwait(false);
        }
        else
        {
            room.MarkRead();
        }

        return state;
    }


    /// <summary> Leaves <paramref name="name" />. Its messages still waiting to be sent fail. </summary>
    public async Task LeaveRoomAsync( string name, CancellationToken token = default )
    {
        ChatRoom? room = Rooms.Find(name);
        if ( room is null )
        {
            return;
        }

        room.IsJoined = false;
        room.ClearUsers();
        FailOutbox(new InvalidOperationException($"Left '{name}'."), name);
        __typingSent.TryRemove(name, out _);

        // A lost connection already took us out of the room; the reconnection won't rejoin it.
        if ( !IsReady )
        {
            return;
        }

        await __sync.WaitAsync(token).ConfigureAwait(false);

        try { await Connection.InvokeAsync(nameof(IChatHub.LeaveRoom), name, token).ConfigureAwait(false); }
        finally { __sync.Release(); }
    }


    /// <summary>
    ///     Tells the room the user is typing; call it on every keystroke, it sends at most every half
    ///     <see cref="ChatClientOptions.TypingTimeout" />, and nothing while disconnected.
    /// </summary>
    public async Task TypingAsync( string name, CancellationToken token = default )
    {
        if ( !IsReady || Rooms.Find(name) is not { IsJoined: true } )
        {
            return;
        }

        long now      = Options.Clock.GetTimestamp();
        long interval = (long)( Options.TypingTimeout.TotalSeconds / 2 * Options.Clock.TimestampFrequency );
        long last     = __typingSent.GetValueOrDefault(name, long.MinValue);
        if ( last != long.MinValue && now - last < interval )
        {
            return;
        }

        __typingSent[name] = now;

        try { await Connection.SendAsync(nameof(IChatHub.Typing), name, token).ConfigureAwait(false); }
        catch ( Exception e ) when ( e is not OperationCanceledException ) { _Logger.LogDebug(e, "Couldn't send typing to {Room}.", name); }
    }


    public ChatRoom GetOrAddRoom( string name )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock ( __lock )
        {
            ChatRoom? room = Rooms.Find(name);
            if ( room is not null )
            {
                return room;
            }

            room = new ChatRoom(name);
            Rooms.Add(room);
            return room;
        }
    }

    private ChatRoom[] GetRooms( bool joinedOnly )
    {
        var rooms = Rooms.ToArray();

        return joinedOnly
                   ? Array.FindAll(rooms, static room => room.IsJoined)
                   : rooms;
    }


    // ─── Messages ────────────────────────────────────────────────────────────

    /// <summary>
    ///     Sends <paramref name="text" /> and <paramref name="attachments" /> to the joined room
    ///     <paramref name="room" />.
    /// </summary>
    /// <inheritdoc cref="SendAsync(InstantMessage, CancellationToken)" />
    public Task<InstantMessage> SendAsync( string room, string text, IEnumerable<FileData>? attachments = null, CancellationToken token = default ) =>
        SendAsync(InstantMessage.Create(room,
                                        text,
                                        attachments is null
                                            ? null
                                            : [.. attachments]),
                  token);


    /// <summary>
    ///     Queues <paramref name="message" /> (it shows in its room right away, as pending) and completes with the server's
    ///     copy once it's stored. While disconnected it waits and is resent after the
    ///     reconnection; a resend the server already has isn't posted twice. Fails with the server's
    ///     <see cref="HubException" /> when it's refused (too large, not in the room, ...).
    /// </summary>
    /// <remarks> Canceling <paramref name="token" /> stops waiting, and stops the message unless it's already on its way. </remarks>
    public async Task<InstantMessage> SendAsync( InstantMessage message, CancellationToken token = default )
    {
        ArgumentNullException.ThrowIfNull(message);
        ObjectDisposedException.ThrowIf(__disposed, this);

        if ( message.ClientID == Guid.Empty )
        {
            throw new ArgumentException($"The message has no {nameof(InstantMessage.ClientID)}: create it with {nameof(InstantMessage)}.{nameof(InstantMessage.Create)}.", nameof(message));
        }

        ChatRoom room = Rooms.Find(message.Room) is { IsJoined: true } joined
                            ? joined
                            : throw new InvalidOperationException($"Join '{message.Room}' before sending to it.");

        Pending pending = __outbox.GetOrAdd(message.ClientID, static ( _, state ) => new Pending(state.message, Interlocked.Increment(ref state.client.__order)), ( message, client: this ));
        room.AddPending(message.Stamp(Guid.Empty, User ?? __requested ?? ChatUser.Empty, Options.Clock.GetUtcNow()));
        _ = FlushAsync();

        try { return await pending.Completion.Task.WaitAsync(token).ConfigureAwait(false); }
        catch ( OperationCanceledException ) when ( token.IsCancellationRequested )
        {
            if ( !pending.InFlight && __outbox.TryRemove(new KeyValuePair<Guid, Pending>(message.ClientID, pending)) )
            {
                room.RemovePending(message.ClientID);
                pending.Completion.TrySetCanceled(token);
            }

            throw;
        }
    }


    /// <summary>
    ///     Pages in the messages before the oldest one <paramref name="name" /> has (or its latest, when it has none).
    ///     Returns the page; an empty one means there's nothing older.
    /// </summary>
    public async Task<InstantMessage[]> LoadOlderAsync( string name, CancellationToken token = default )
    {
        ChatRoom room   = Rooms.Find(name) ?? throw new InvalidOperationException($"Join '{name}' first.");
        long?    oldest = room.GetOldestSequence();

        HistoryQuery query = oldest is { } before
                                 ? HistoryQuery.Older(name, before, Options.HistoryPageSize)
                                 : HistoryQuery.Latest(name, Options.HistoryPageSize);

        return await HistoryAsync(query, token).ConfigureAwait(false);
    }


    /// <summary> A page of history (<see cref="HistoryQuery" />), also added to its room. </summary>
    public async Task<InstantMessage[]> HistoryAsync( HistoryQuery query, CancellationToken token = default )
    {
        ArgumentNullException.ThrowIfNull(query);
        await WaitUntilReadyAsync(token).ConfigureAwait(false);

        var       page = await Connection.InvokeAsync<InstantMessage[]>(nameof(IChatHub.History), query, token).ConfigureAwait(false);
        ChatRoom? room = Rooms.Find(query.Room);

        if ( room is not null )
        {
            foreach ( InstantMessage message in page )
            {
                Deliver(room, message, false);
            }
        }

        return page;
    }


    /// <summary> Fetches everything after <paramref name="after" />, a page at a time. </summary>
    private async Task CatchUpAsync( ChatRoom room, long after, CancellationToken token )
    {
        long cursor = after;

        while ( true )
        {
            var page = await Connection.InvokeAsync<InstantMessage[]>(nameof(IChatHub.History), HistoryQuery.Newer(room.Name, cursor, Options.HistoryPageSize), token).ConfigureAwait(false);
            if ( page.Length == 0 )
            {
                return;
            }

            foreach ( InstantMessage message in page )
            {
                Deliver(room, message, true);
            }

            long last = page[^1].Sequence;
            if ( last <= cursor )
            {
                return;
            }

            cursor = last;
        }
    }


    private void Deliver( ChatRoom room, InstantMessage message, bool live )
    {
        if ( room.Apply(message, User, live) && live )
        {
            MessageReceived?.Invoke(this, message);
        }
    }


    private async Task FlushAsync()
    {
        // Loops because a message queued while another flush was finishing would otherwise wait for the next one.
        while ( !__disposed && IsReady && !__outbox.IsEmpty )
        {
            if ( !await __flush.WaitAsync(0).ConfigureAwait(false) )
            {
                return;
            }

            bool keepGoing;

            try { keepGoing = await FlushOnceAsync().ConfigureAwait(false); }
            finally { __flush.Release(); }

            if ( !keepGoing )
            {
                return;
            }
        }
    }

    private async Task<bool> FlushOnceAsync()
    {
        Pending[] pending = [.. __outbox.Values];
        Array.Sort(pending, static ( left, right ) => left.Order.CompareTo(right.Order));

        foreach ( Pending item in pending )
        {
            if ( !IsReady )
            {
                return false;
            }

            item.InFlight = true;

            try
            {
                InstantMessage stored = await Connection.InvokeAsync<InstantMessage>(nameof(IChatHub.Send), item.Message).ConfigureAwait(false);
                __outbox.TryRemove(item.Message.ClientID, out _);

                if ( Rooms.Find(stored.Room) is { } room )
                {
                    Deliver(room, stored, true);
                }

                item.Completion.TrySetResult(stored);
            }
            catch ( HubException e )
            {
                __outbox.TryRemove(item.Message.ClientID, out _);
                Rooms.Find(item.Message.Room)?.RemovePending(item.Message.ClientID);
                item.Completion.TrySetException(e);
            }
            catch ( Exception e )
            {
                // The connection went away; the message stays queued for the reconnection.
                item.InFlight = false;
                _Logger.LogDebug(e, "Couldn't send a message; it will be resent.");
                return false;
            }
        }

        return true;
    }

    private void FailOutbox( Exception error, string? room = null )
    {
        foreach ( Pending item in __outbox.Values )
        {
            if ( room is not null && !string.Equals(item.Message.Room, room, StringComparison.Ordinal) )
            {
                continue;
            }

            if ( !__outbox.TryRemove(new KeyValuePair<Guid, Pending>(item.Message.ClientID, item)) )
            {
                continue;
            }

            Rooms.Find(item.Message.Room)?.RemovePending(item.Message.ClientID);
            item.Completion.TrySetException(error);
        }
    }


    // ─── Server callbacks ────────────────────────────────────────────────────

    protected virtual void OnReceived( InstantMessage message )
    {
        if ( Rooms.Find(message.Room) is { } room )
        {
            Deliver(room, message, true);
        }
    }

    protected virtual void OnHubEvent( HubEvent value )
    {
        ChatRoom? room = Rooms.Find(value.Room);

        switch ( value.Type )
        {
            case HubEventType.Typing:
                if ( !value.User.Equals(User) )
                {
                    room?.Typing(value.User, Options.TypingTimeout, Options.Clock);
                }

                break;

            case HubEventType.JoinRoom:
            case HubEventType.Reconnection:
                room?.AddUser(value.User);
                break;

            case HubEventType.LeaveRoom:
            case HubEventType.Disconnection:
                room?.RemoveUser(value.User);
                break;

            case HubEventType.Logout:
                foreach ( ChatRoom each in GetRooms(false) )
                {
                    each.RemoveUser(value.User);
                }

                break;
        }

        OnEvent?.Invoke(this, value);
    }

    protected void OnError( Exception error ) { Error?.Invoke(this, error); }



    private sealed class Pending( InstantMessage message, long order )
    {
        public volatile bool InFlight;

        public TaskCompletionSource<InstantMessage> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public InstantMessage Message { get; } = message;
        public long           Order   { get; } = order;
    }



    private sealed class RetryPolicy( ChatClientOptions options ) : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay( RetryContext context ) => options.GetRetryDelay(context.PreviousRetryCount);
    }



    /// <summary> The <see cref="IChatClient" /> side: one handler per method, registered by name. </summary>
    private sealed class Callbacks( ChatClient client ) : IChatClient
    {
        public Task Received( InstantMessage message ) { return Run(() => client.OnReceived(message)); }

        public Task Typing( HubEvent value ) { return Run(() => client.OnHubEvent(value)); }

        public Task JoinedRoom( HubEvent value ) { return Run(() => client.OnHubEvent(value)); }

        public Task LeftRoom( HubEvent value ) { return Run(() => client.OnHubEvent(value)); }

        public Task LoggedIn( HubEvent value ) { return Run(() => client.OnHubEvent(value)); }

        public Task LoggedOut( HubEvent value ) { return Run(() => client.OnHubEvent(value)); }

        public Task Disconnected( HubEvent value ) { return Run(() => client.OnHubEvent(value)); }

        public Task Reconnected( HubEvent value ) { return Run(() => client.OnHubEvent(value)); }


        private Task Run( Action action )
        {
            try { action(); }
            catch ( Exception e )
            {
                // A failing event handler mustn't take the connection's receive loop down with it.
                client._Logger.LogError(e, "A chat event handler failed.");
                client.OnError(e);
            }

            return Task.CompletedTask;
        }
    }
}
