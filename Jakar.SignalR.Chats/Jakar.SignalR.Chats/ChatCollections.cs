// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

namespace Jakar.SignalR.Chats;


[Serializable]
[JsonConverter(typeof(CollectionAlertsJsonConverter<ChatUserCollection, ChatUser>))]
[JsonModel(typeof(ChatClientJsonContext))]
public sealed partial class ChatUserCollection : ConcurrentObservableCollection<ChatUserCollection, ChatUser>, ICollectionAlerts<ChatUserCollection, ChatUser>
{
    public ChatUserCollection() : this(DEFAULT_CAPACITY) { }

    public ChatUserCollection( int capacity ) : base(capacity) { }

    public ChatUserCollection( IEnumerable<ChatUser> enumerable ) : base(enumerable) { }

    public ChatUserCollection( params ReadOnlySpan<ChatUser> enumerable ) : base(enumerable) { }


    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override bool Equals( object? other ) => ReferenceEquals(this, other) || ( other is ChatUserCollection x && Equals(x) );

    public static bool operator ==( ChatUserCollection? left, ChatUserCollection? right ) => EqualityComparer<ChatUserCollection>.Default.Equals(left, right);

    public static bool operator !=( ChatUserCollection? left, ChatUserCollection? right ) => !EqualityComparer<ChatUserCollection>.Default.Equals(left, right);

    public static bool operator >( ChatUserCollection left, ChatUserCollection right ) => Comparer<ChatUserCollection>.Default.Compare(left, right) > 0;

    public static bool operator >=( ChatUserCollection left, ChatUserCollection right ) => Comparer<ChatUserCollection>.Default.Compare(left, right) >= 0;

    public static bool operator <( ChatUserCollection left, ChatUserCollection right ) => Comparer<ChatUserCollection>.Default.Compare(left, right) < 0;

    public static bool operator <=( ChatUserCollection left, ChatUserCollection right ) => Comparer<ChatUserCollection>.Default.Compare(left, right) <= 0;


    public static implicit operator ChatUserCollection( List<ChatUser> values ) => new(values);

    public static implicit operator ChatUserCollection( HashSet<ChatUser> values ) => new(values);

    public static implicit operator ChatUserCollection( ConcurrentBag<ChatUser> values ) => new(values);

    public static implicit operator ChatUserCollection( Collection<ChatUser> values ) => new(values);

    public static implicit operator ChatUserCollection( ChatUser[] values ) => new(values.AsSpan());

    public static implicit operator ChatUserCollection( ImmutableArray<ChatUser> values ) => new(values.AsSpan());

    public static implicit operator ChatUserCollection( ReadOnlyMemory<ChatUser> values ) => new(values.Span);

    public static implicit operator ChatUserCollection( ReadOnlySpan<ChatUser> values ) => new(values);
}



/// <summary> A room's messages, in <see cref="InstantMessage.Sequence" /> order; messages still being sent come last. </summary>
[Serializable]
[JsonConverter(typeof(CollectionAlertsJsonConverter<InstantMessageCollection, InstantMessage>))]
[JsonModel(typeof(ChatClientJsonContext))]
public sealed partial class InstantMessageCollection : ConcurrentObservableCollection<InstantMessageCollection, InstantMessage>, ICollectionAlerts<InstantMessageCollection, InstantMessage>
{
    public InstantMessageCollection() : this(DEFAULT_CAPACITY) { }

    public InstantMessageCollection( int capacity ) : base(capacity) { }

    public InstantMessageCollection( IEnumerable<InstantMessage> enumerable ) : base(enumerable) { }

    public InstantMessageCollection( params ReadOnlySpan<InstantMessage> enumerable ) : base(enumerable) { }


    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override bool Equals( object? other ) => ReferenceEquals(this, other) || ( other is InstantMessageCollection x && Equals(x) );

    public static bool operator ==( InstantMessageCollection? left, InstantMessageCollection? right ) => EqualityComparer<InstantMessageCollection>.Default.Equals(left, right);

    public static bool operator !=( InstantMessageCollection? left, InstantMessageCollection? right ) => !EqualityComparer<InstantMessageCollection>.Default.Equals(left, right);

    public static bool operator >( InstantMessageCollection left, InstantMessageCollection right ) => Comparer<InstantMessageCollection>.Default.Compare(left, right) > 0;

    public static bool operator >=( InstantMessageCollection left, InstantMessageCollection right ) => Comparer<InstantMessageCollection>.Default.Compare(left, right) >= 0;

    public static bool operator <( InstantMessageCollection left, InstantMessageCollection right ) => Comparer<InstantMessageCollection>.Default.Compare(left, right) < 0;

    public static bool operator <=( InstantMessageCollection left, InstantMessageCollection right ) => Comparer<InstantMessageCollection>.Default.Compare(left, right) <= 0;


    public static implicit operator InstantMessageCollection( List<InstantMessage> values ) => new(values);

    public static implicit operator InstantMessageCollection( HashSet<InstantMessage> values ) => new(values);

    public static implicit operator InstantMessageCollection( ConcurrentBag<InstantMessage> values ) => new(values);

    public static implicit operator InstantMessageCollection( Collection<InstantMessage> values ) => new(values);

    public static implicit operator InstantMessageCollection( InstantMessage[] values ) => new(values.AsSpan());

    public static implicit operator InstantMessageCollection( ImmutableArray<InstantMessage> values ) => new(values.AsSpan());

    public static implicit operator InstantMessageCollection( ReadOnlyMemory<InstantMessage> values ) => new(values.Span);

    public static implicit operator InstantMessageCollection( ReadOnlySpan<InstantMessage> values ) => new(values);
}



/// <summary>
///     The rooms a <see cref="ChatClient" /> knows. Enumerating it shows the joined ones only, unless
///     <see cref="ShowAll" />; JSON always has all of them.
/// </summary>
[Serializable]
[JsonConverter(typeof(CollectionAlertsJsonConverter<ChatRoomCollection, ChatRoom>))]
[JsonModel(typeof(ChatClientJsonContext))]
public sealed partial class ChatRoomCollection : ConcurrentObservableCollection<ChatRoomCollection, ChatRoom>, ICollectionAlerts<ChatRoomCollection, ChatRoom>
{
    public ChatRoomCollection() : this(DEFAULT_CAPACITY) { }

    public ChatRoomCollection( int capacity ) : base(capacity) { }

    public ChatRoomCollection( IEnumerable<ChatRoom> enumerable ) : base(enumerable) { }

    public ChatRoomCollection( params ReadOnlySpan<ChatRoom> enumerable ) : base(enumerable) { }

    [JsonIgnore] public bool ShowAll { get; set => SetProperty(ref field, value); }


    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public override bool Equals( object? other ) => ReferenceEquals(this, other) || ( other is ChatRoomCollection x && Equals(x) );

    public static bool operator ==( ChatRoomCollection? left, ChatRoomCollection? right ) => EqualityComparer<ChatRoomCollection>.Default.Equals(left, right);

    public static bool operator !=( ChatRoomCollection? left, ChatRoomCollection? right ) => !EqualityComparer<ChatRoomCollection>.Default.Equals(left, right);

    public static bool operator >( ChatRoomCollection left, ChatRoomCollection right ) => Comparer<ChatRoomCollection>.Default.Compare(left, right) > 0;

    public static bool operator >=( ChatRoomCollection left, ChatRoomCollection right ) => Comparer<ChatRoomCollection>.Default.Compare(left, right) >= 0;

    public static bool operator <( ChatRoomCollection left, ChatRoomCollection right ) => Comparer<ChatRoomCollection>.Default.Compare(left, right) < 0;

    public static bool operator <=( ChatRoomCollection left, ChatRoomCollection right ) => Comparer<ChatRoomCollection>.Default.Compare(left, right) <= 0;


    public static implicit operator ChatRoomCollection( List<ChatRoom> values ) => new(values);

    public static implicit operator ChatRoomCollection( HashSet<ChatRoom> values ) => new(values);

    public static implicit operator ChatRoomCollection( ConcurrentBag<ChatRoom> values ) => new(values);

    public static implicit operator ChatRoomCollection( Collection<ChatRoom> values ) => new(values);

    public static implicit operator ChatRoomCollection( ChatRoom[] values ) => new(values.AsSpan());

    public static implicit operator ChatRoomCollection( ImmutableArray<ChatRoom> values ) => new(values.AsSpan());

    public static implicit operator ChatRoomCollection( ReadOnlyMemory<ChatRoom> values ) => new(values.Span);

    public static implicit operator ChatRoomCollection( ReadOnlySpan<ChatRoom> values ) => new(values);


    protected override bool Filter( int index, ref readonly ChatRoom? value ) => ShowAll || value?.IsJoined is true;


    /// <summary> The room named <paramref name="name" />, joined or not. </summary>
    public ChatRoom? Find( string name )
    {
        int index = FindIndex(( ref readonly ChatRoom room ) => string.Equals(room.Name, name, StringComparison.Ordinal));

        return index >= 0
                   ? Get(index)
                   : null;
    }
}
