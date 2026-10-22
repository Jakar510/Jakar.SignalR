// TrueLogic :: TrueLogic.Common
// 05/11/2022  12:24 PM

namespace Jakar.SignalR.Chats;


/// <summary> A chat message, with optional file attachments. </summary>
/// <remarks>
///     The sender sets <see cref="ClientID" />, <see cref="Room" />, <see cref="Text" /> and <see cref="Attachments" />.
///     The server sets everything else:
///     <see cref="ID" />, <see cref="Sender" /> (from the logged-in connection, never from the client),
///     <see cref="TimeStamp" />, and <see cref="Sequence" />,
///     which orders a room's messages and is the cursor for <see cref="IChatHub.History" />.
///     <para>
///         <see cref="ClientID" /> makes sending idempotent: resending a message with the same <see cref="ClientID" />
///         returns the stored copy instead of posting it twice. Two messages are equal when their <see cref="ClientID" />s
///         are.
///     </para>
/// </remarks>
[Serializable]
[JsonModel(typeof(ChatJsonContext))]
public sealed partial class InstantMessage : BaseClass<InstantMessage>, IEqualComparable<InstantMessage>
{
    public static readonly InstantMessage[] Empty = [];


    public                           FileData[]?    Attachments { get; init; }
    public                           Guid           ClientID    { get; init; }
    public                           Guid           ID          { get; init; }
    [StringLength(NAME)] public      string         Room        { get; init; } = EMPTY;
    public                           ChatUser       Sender      { get; init; } = ChatUser.Empty;
    public                           long           Sequence    { get; init; }
    [StringLength(MAX_FIXED)] public string         Text        { get; init; } = EMPTY;
    public                           DateTimeOffset TimeStamp   { get; init; }


    public override bool Equals( InstantMessage? other ) => other is not null && ( ReferenceEquals(this, other) || ClientID.Equals(other.ClientID) );
    public override bool Equals( object?         other ) => ReferenceEquals(this, other) || ( other is InstantMessage message && Equals(message) );
    public override int  GetHashCode()                   => ClientID.GetHashCode();

    public override int CompareTo( InstantMessage? other )
    {
        if ( other is null ) { return 1; }

        if ( ReferenceEquals(this, other) ) { return 0; }

        // Messages the server hasn't sequenced yet (Sequence 0: still being sent) sort after the ones it has.
        int sequenceComparison = sequenced(Sequence).CompareTo(sequenced(other.Sequence));
        if ( sequenceComparison != 0 ) { return sequenceComparison; }

        int timeStampComparison = TimeStamp.CompareTo(other.TimeStamp);
        if ( timeStampComparison != 0 ) { return timeStampComparison; }

        return ClientID.CompareTo(other.ClientID);

        static long sequenced( long sequence ) =>
            sequence > 0
                ? sequence
                : long.MaxValue;
    }


    public static bool operator ==( InstantMessage? left, InstantMessage? right ) => EqualityComparer<InstantMessage>.Default.Equals(left, right);

    public static bool operator !=( InstantMessage? left, InstantMessage? right ) => !EqualityComparer<InstantMessage>.Default.Equals(left, right);

    public static bool operator >( InstantMessage left, InstantMessage right ) => Comparer<InstantMessage>.Default.Compare(left, right) > 0;

    public static bool operator >=( InstantMessage left, InstantMessage right ) => Comparer<InstantMessage>.Default.Compare(left, right) >= 0;

    public static bool operator <( InstantMessage left, InstantMessage right ) => Comparer<InstantMessage>.Default.Compare(left, right) < 0;

    public static bool operator <=( InstantMessage left, InstantMessage right ) => Comparer<InstantMessage>.Default.Compare(left, right) <= 0;


    /// <summary> A new message for <paramref name="room" />, with a fresh <see cref="ClientID" />. </summary>
    public static InstantMessage Create( string room, string text, params FileData[]? attachments ) =>
        new()
        {
            ClientID = Guid.CreateVersion7(),
            Room     = room,
            Text     = text,
            Attachments = attachments is { Length: > 0 }
                              ? attachments
                              : null
        };


    /// <summary> A copy with the server-assigned values. </summary>
    public InstantMessage Stamp( Guid id, ChatUser sender, DateTimeOffset timeStamp, long sequence = 0 ) =>
        new()
        {
            Attachments = Attachments,
            ClientID    = ClientID,
            ID          = id,
            Room        = Room,
            Sender      = sender,
            Sequence    = sequence,
            Text        = Text,
            TimeStamp   = timeStamp
        };

    /// <summary> A copy with <see cref="Sequence" /> set (by an <c>IChatStore</c>). </summary>
    public InstantMessage WithSequence( long sequence ) => Stamp(ID, Sender, TimeStamp, sequence);


    /// <summary>
    ///     The total length of the attachments' payloads, in characters (binary files travel as base64, about 4/3 of
    ///     their size).
    /// </summary>
    public long GetAttachmentsLength()
    {
        if ( Attachments is null ) { return 0; }

        long size = 0;
        foreach ( FileData file in Attachments ) { size += file.Payload.Length; }

        return size;
    }
}
