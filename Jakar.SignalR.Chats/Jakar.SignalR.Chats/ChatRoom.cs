// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

namespace Jakar.SignalR.Chats;


/// <summary>
///     A client's view of one room: its messages, who is in it, who is typing, and what hasn't been read yet.
///     <see cref="ChatClient" /> keeps it up to date.
/// </summary>
/// <remarks>
///     Changes arrive on SignalR's threads: the collections are thread safe and raise their notifications there, so a UI
///     marshals them to its own thread.
///     It serializes (to keep a room's messages between runs) without <see cref="Users" /> and <see cref="TypingUsers" />,
///     which only mean something while connected.
/// </remarks>
[Serializable]
[JsonModel(typeof(ChatClientJsonContext))]
public sealed partial class ChatRoom : BaseClass<ChatRoom>, IEqualComparable<ChatRoom>
{
    private readonly Lock                   __lock   = new();
    private readonly Dictionary<Guid, long> __typing = [];


    public ChatRoom() { }

    public ChatRoom( string name ) => Name = name;


    /// <summary>
    ///     The room the user is looking at: its messages don't count as unread, and making it active marks everything
    ///     read.
    /// </summary>
    [JsonIgnore] public bool IsActive
    {
        get;
        set
        {
            if ( SetProperty(ref field, value) && value )
            {
                MarkRead();
            }
        }
    }

    /// <summary>
    ///     The user wants to be in this room: <see cref="ChatClient" /> rejoins it after every reconnection until it's
    ///     left.
    /// </summary>
    public bool IsJoined { get; set => SetProperty(ref field, value); }

    /// <summary> The highest <see cref="InstantMessage.Sequence" /> received: where catching up after a reconnection starts. </summary>
    public long LastSequence { get; set => SetProperty(ref field, value); }

    public long LastReadSequence { get; set => SetProperty(ref field, value); }

    public InstantMessageCollection Messages { get; init; } = new();

    [StringLength(NAME)] public string Name { get; init; } = EMPTY;

    [JsonIgnore] public ChatUserCollection TypingUsers { get; } = new();

    public int UnreadChats { get; set => SetProperty(ref field, value); }

    [JsonIgnore] public ChatUserCollection Users { get; } = new();


    public override bool Equals( ChatRoom? other ) => other is not null && ( ReferenceEquals(this, other) || string.Equals(Name, other.Name, StringComparison.Ordinal) );

    public override bool Equals( object? other ) => ReferenceEquals(this, other) || ( other is ChatRoom room && Equals(room) );

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);

    public override int CompareTo( ChatRoom? other ) =>
        other is null
            ? 1
            : string.Compare(Name, other.Name, StringComparison.Ordinal);


    public static bool operator ==( ChatRoom? left, ChatRoom? right ) => Equals(left, right);

    public static bool operator !=( ChatRoom? left, ChatRoom? right ) => !Equals(left, right);

    public static bool operator <( ChatRoom? left, ChatRoom? right ) => Comparer<ChatRoom>.Default.Compare(left, right) < 0;

    public static bool operator >( ChatRoom? left, ChatRoom? right ) => Comparer<ChatRoom>.Default.Compare(left, right) > 0;

    public static bool operator <=( ChatRoom? left, ChatRoom? right ) => Comparer<ChatRoom>.Default.Compare(left, right) <= 0;

    public static bool operator >=( ChatRoom? left, ChatRoom? right ) => Comparer<ChatRoom>.Default.Compare(left, right) >= 0;


    /// <summary>
    ///     A message was added to <see cref="Messages" /> (not raised for the user's own messages while they're being
    ///     sent).
    /// </summary>
    public event EventHandler<InstantMessage>? MessageReceived;


    public void MarkRead()
    {
        lock ( __lock )
        {
            LastReadSequence = LastSequence;
            UnreadChats      = 0;
        }
    }


    /// <summary> The oldest sequenced message, for paging back through history. </summary>
    public long? GetOldestSequence()
    {
        lock ( __lock )
        {
            for ( int i = 0; i < Messages.Count; i++ )
            {
                long sequence = Messages.Get(i).Sequence;
                if ( sequence > 0 )
                {
                    return sequence;
                }
            }

            return null;
        }
    }


    /// <summary>
    ///     Adds <paramref name="message" />, or replaces the copy that was waiting to be sent. Messages already here are
    ///     ignored.
    /// </summary>
    /// <returns> <see langword="true" /> when the room didn't have it yet. </returns>
    internal bool Apply( InstantMessage message, ChatUser? user, bool live = true )
    {
        bool added;

        lock ( __lock )
        {
            int index = Messages.IndexOf(message);

            if ( index >= 0 )
            {
                InstantMessage existing = Messages.Get(index);
                if ( existing.Sequence > 0 || message.Sequence <= 0 )
                {
                    return false;
                }

                // The server's copy of a message we sent: it moves from the pending tail to its place in the sequence.
                Messages.RemoveAt(index);
                Insert(message);
                added = false;
            }
            else
            {
                Insert(message);
                added = true;
            }

            if ( message.Sequence > LastSequence )
            {
                LastSequence = message.Sequence;

                if ( IsActive )
                {
                    LastReadSequence = message.Sequence;
                }
                else if ( added && live && !message.Sender.Equals(user) )
                {
                    UnreadChats++;
                }
            }

            StopTyping(message.Sender);
        }

        if ( added && live )
        {
            MessageReceived?.Invoke(this, message);
        }

        return added;
    }

    /// <summary> Shows a message that's still being sent (at the end, until the server's copy replaces it). </summary>
    internal void AddPending( InstantMessage message )
    {
        lock ( __lock )
        {
            if ( Messages.IndexOf(message) < 0 )
            {
                Insert(message);
            }
        }
    }

    internal void RemovePending( Guid clientID )
    {
        lock ( __lock ) { Messages.Remove(( ref readonly InstantMessage message ) => message.ClientID == clientID && message.Sequence <= 0); }
    }

    private void Insert( InstantMessage message )
    {
        // Messages arrive almost always in order, so look from the end.
        int index = Messages.Count;
        while ( index > 0 && Messages.Get(index - 1).CompareTo(message) > 0 )
        {
            index--;
        }

        Messages.Insert(index, [message]);
    }


    internal void SetUsers( ChatUser[] users )
    {
        lock ( __lock )
        {
            Users.Clear();
            Users.Add(users);
        }
    }

    internal void AddUser( ChatUser user )
    {
        lock ( __lock )
        {
            if ( !Users.Contains(user) )
            {
                Users.Add(user);
            }
        }
    }

    internal void RemoveUser( ChatUser user )
    {
        lock ( __lock )
        {
            Users.Remove(user);
            StopTyping(user);
        }
    }

    internal void ClearUsers()
    {
        lock ( __lock )
        {
            Users.Clear();
            TypingUsers.Clear();
            __typing.Clear();
        }
    }


    /// <summary> Shows <paramref name="user" /> as typing for <paramref name="timeout" />, or until their message arrives. </summary>
    internal void Typing( ChatUser user, TimeSpan timeout, TimeProvider clock )
    {
        long until = clock.GetTimestamp() + (long)( timeout.TotalSeconds * clock.TimestampFrequency );

        lock ( __lock )
        {
            __typing[user.UserID] = until;
            if ( !TypingUsers.Contains(user) )
            {
                TypingUsers.Add(user);
            }
        }

        _ = ExpireTypingAsync(user, timeout, clock);
    }

    private async Task ExpireTypingAsync( ChatUser user, TimeSpan timeout, TimeProvider clock )
    {
        await Task.Delay(timeout, clock).ConfigureAwait(false);

        lock ( __lock )
        {
            // Typing again since then moved the deadline.
            if ( __typing.TryGetValue(user.UserID, out long until) && clock.GetTimestamp() >= until )
            {
                StopTyping(user);
            }
        }
    }

    private void StopTyping( ChatUser user )
    {
        if ( __typing.Remove(user.UserID) )
        {
            TypingUsers.Remove(user);
        }
    }
}
