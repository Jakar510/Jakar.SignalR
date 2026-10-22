// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

namespace Jakar.SignalR.Chats;


/// <summary> What a client needs after joining (or rejoining) a room: who is in it, and its latest messages. </summary>
[Serializable]
[JsonModel(typeof(ChatJsonContext))]
public sealed partial record ChatRoomState
{
    public                      InstantMessage[] Messages { get; init; } = InstantMessage.Empty;
    [StringLength(NAME)] public string           Room     { get; init; } = EMPTY;
    public                      ChatUser[]       Users    { get; init; } = [];
}



/// <summary> A page of a room's history, by <see cref="InstantMessage.Sequence" />. </summary>
/// <remarks>
///     With <see cref="After" />: the oldest <see cref="Count" /> messages after it (catching up after a reconnection).
///     Otherwise: the newest <see cref="Count" /> messages before <see cref="Before" />, or the newest overall without it
///     (scrolling back).
///     Either way the page is in ascending <see cref="InstantMessage.Sequence" /> order.
/// </remarks>
[Serializable]
[JsonModel(typeof(ChatJsonContext))]
public sealed partial record HistoryQuery
{
    public const int DEFAULT_COUNT = 50;


    public                      long?  After  { get; init; }
    public                      long?  Before { get; init; }
    public                      int    Count  { get; init; } = DEFAULT_COUNT;
    [StringLength(NAME)] public string Room   { get; init; } = EMPTY;


    public static HistoryQuery Latest( string room, int count = DEFAULT_COUNT ) =>
        new()
        {
            Room  = room,
            Count = count
        };

    public static HistoryQuery Older( string room, long before, int count = DEFAULT_COUNT ) =>
        new()
        {
            Room   = room,
            Before = before,
            Count  = count
        };

    public static HistoryQuery Newer( string room, long after, int count = DEFAULT_COUNT ) =>
        new()
        {
            Room  = room,
            After = after,
            Count = count
        };
}
