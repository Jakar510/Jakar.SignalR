// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 09/25/2025  10:19

namespace Jakar.SignalR.Chats;


public enum HubEventType : byte
{
    /// <summary> A user came online (their first connection logged in). <see cref="HubEvent.Room" /> is empty. </summary>
    Login,

    /// <summary> A user went offline on purpose (their last connection logged out). <see cref="HubEvent.Room" /> is empty. </summary>
    Logout,

    /// <summary> A user is typing in <see cref="HubEvent.Room" />. </summary>
    Typing,

    /// <summary> A user joined <see cref="HubEvent.Room" />. </summary>
    JoinRoom,

    /// <summary> A user left <see cref="HubEvent.Room" />. </summary>
    LeaveRoom,

    /// <summary> A user's connection to <see cref="HubEvent.Room" /> came back after it was lost. </summary>
    Reconnection,

    /// <summary> A user's connection to <see cref="HubEvent.Room" /> was lost (they didn't leave or log out). </summary>
    Disconnection
}



/// <summary>
///     Something a user did in a room (or, for <see cref="HubEventType.Login" /> /
///     <see cref="HubEventType.Logout" />, in the chat as a whole). Messages arrive as <see cref="InstantMessage" />s
///     instead.
/// </summary>
[Serializable]
[JsonModel(typeof(ChatJsonContext))]
public sealed partial class HubEvent : BaseClass<HubEvent>, IEqualComparable<HubEvent>
{
    public HubEvent() { }

    public HubEvent( HubEventType type, string room, ChatUser user, DateTimeOffset timeStamp )
    {
        Type      = type;
        Room      = room;
        User      = user;
        TimeStamp = timeStamp;
    }

    [StringLength(NAME)] public string         Room      { get; init; } = EMPTY;
    public                      DateTimeOffset TimeStamp { get; init; }
    public                      HubEventType   Type      { get; init; }
    public                      ChatUser       User      { get; init; } = ChatUser.Empty;


    public override int CompareTo( HubEvent? other )
    {
        if ( ReferenceEquals(this, other) )
        {
            return 0;
        }

        if ( other is null )
        {
            return 1;
        }

        int timeStampComparison = TimeStamp.CompareTo(other.TimeStamp);
        if ( timeStampComparison != 0 )
        {
            return timeStampComparison;
        }

        int roomComparison = string.Compare(Room, other.Room, StringComparison.Ordinal);
        if ( roomComparison != 0 )
        {
            return roomComparison;
        }

        int typeComparison = Type.CompareTo(other.Type);
        if ( typeComparison != 0 )
        {
            return typeComparison;
        }

        return User.CompareTo(other.User);
    }

    public override bool Equals( HubEvent? other )
    {
        if ( other is null )
        {
            return false;
        }

        if ( ReferenceEquals(this, other) )
        {
            return true;
        }

        return Type == other.Type && TimeStamp.Equals(other.TimeStamp) && string.Equals(Room, other.Room, StringComparison.Ordinal) && User.Equals(other.User);
    }

    public override bool Equals( object? other ) => ReferenceEquals(this, other) || ( other is HubEvent value && Equals(value) );

    public override int GetHashCode() => HashCode.Combine(Type, TimeStamp, Room, User);


    public static bool operator <( HubEvent? left, HubEvent? right ) => Comparer<HubEvent>.Default.Compare(left, right) < 0;

    public static bool operator >( HubEvent? left, HubEvent? right ) => Comparer<HubEvent>.Default.Compare(left, right) > 0;

    public static bool operator <=( HubEvent? left, HubEvent? right ) => Comparer<HubEvent>.Default.Compare(left, right) <= 0;

    public static bool operator >=( HubEvent? left, HubEvent? right ) => Comparer<HubEvent>.Default.Compare(left, right) >= 0;

    public static bool operator ==( HubEvent? left, HubEvent? right ) => EqualityComparer<HubEvent>.Default.Equals(left, right);

    public static bool operator !=( HubEvent? left, HubEvent? right ) => !EqualityComparer<HubEvent>.Default.Equals(left, right);
}
