// TrueLogic :: TrueLogic.Common
// 05/16/2022  2:09 PM

namespace Jakar.SignalR.Chats;


public interface IChatUser : IUserID
{
    [StringLength(FULL_NAME)] public string FullName { get; }
    [StringLength(USER_NAME)] public string UserName { get; }
}



/// <summary>
///     A chat participant. Two users are the same user when their <see cref="UserID" />s match; the names are only
///     for display.
/// </summary>
[Serializable]
[JsonModel(typeof(ChatJsonContext))]
public sealed partial class ChatUser : BaseClass<ChatUser>, IChatUser, IEqualComparable<ChatUser>
{
    public static readonly ChatUser Empty = new();


    public ChatUser() { }

    public ChatUser( Guid userID, string userName, string fullName )
    {
        UserID   = userID;
        UserName = userName;
        FullName = fullName;
    }

    public ChatUser( IChatUser data ) : this(data.UserID, data.UserName, data.FullName) { }

    [StringLength(FULL_NAME)] public string FullName { get; init; } = EMPTY;
    public                           Guid   UserID   { get; init; }
    [StringLength(USER_NAME)] public string UserName { get; init; } = EMPTY;


    public override bool Equals( ChatUser? other ) => other is not null && ( ReferenceEquals(this, other) || UserID.Equals(other.UserID) );

    public override bool Equals( object? other ) => ReferenceEquals(this, other) || ( other is ChatUser user && Equals(user) );

    public override int GetHashCode() => UserID.GetHashCode();

    public override int CompareTo( ChatUser? other )
    {
        if ( other is null )
        {
            return 1;
        }

        if ( ReferenceEquals(this, other) )
        {
            return 0;
        }

        int userNameComparison = string.Compare(UserName, other.UserName, StringComparison.InvariantCultureIgnoreCase);
        if ( userNameComparison != 0 )
        {
            return userNameComparison;
        }

        return UserID.CompareTo(other.UserID);
    }


    public static bool operator ==( ChatUser? left, ChatUser? right ) => Equals(left, right);

    public static bool operator !=( ChatUser? left, ChatUser? right ) => !Equals(left, right);

    public static bool operator <( ChatUser? left, ChatUser? right ) => Comparer<ChatUser>.Default.Compare(left, right) < 0;

    public static bool operator >( ChatUser? left, ChatUser? right ) => Comparer<ChatUser>.Default.Compare(left, right) > 0;

    public static bool operator <=( ChatUser? left, ChatUser? right ) => Comparer<ChatUser>.Default.Compare(left, right) <= 0;

    public static bool operator >=( ChatUser? left, ChatUser? right ) => Comparer<ChatUser>.Default.Compare(left, right) >= 0;

    public static ChatUser Create( IChatUser data ) => new(data);
}
