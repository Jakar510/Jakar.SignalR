// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

namespace Jakar.SignalR.Chats;


/// <summary>
///     Where the chat keeps its messages. Register your own (a database) to keep history across restarts and
///     servers; <see cref="InMemoryChatStore" /> is the default.
/// </summary>
public interface IChatStore
{
    /// <summary>
    ///     Stores <paramref name="message" /> (already stamped by the hub), giving it the room's next
    ///     <see cref="InstantMessage.Sequence" />.
    /// </summary>
    /// <returns>
    ///     The stored message, and whether it is new. When the room already has a message from the same sender with the same
    ///     <see cref="InstantMessage.ClientID" /> (a retry),
    ///     return that one with <c>Added = false</c> and don't store it again.
    /// </returns>
    ValueTask<(InstantMessage Message, bool Added)> AddAsync( InstantMessage message, CancellationToken token = default );

    /// <summary>
    ///     A page of history, as <see cref="HistoryQuery" /> describes, in ascending
    ///     <see cref="InstantMessage.Sequence" /> order. <see cref="HistoryQuery.Count" /> is already limited by the hub.
    /// </summary>
    ValueTask<InstantMessage[]> GetHistoryAsync( HistoryQuery query, CancellationToken token = default );
}



/// <summary>
///     <see cref="IChatStore" /> in this process's memory, keeping the latest <see cref="MaxMessagesPerRoom" />
///     messages of each room.
/// </summary>
public sealed class InMemoryChatStore : IChatStore
{
    public const     int                         DEFAULT_MAX_MESSAGES_PER_ROOM = 10_000;
    private readonly Lock                        __lock                        = new();
    private readonly Dictionary<string, RoomLog> __rooms                       = new(StringComparer.Ordinal);


    public int MaxMessagesPerRoom { get; init; } = DEFAULT_MAX_MESSAGES_PER_ROOM;


    public ValueTask<(InstantMessage Message, bool Added)> AddAsync( InstantMessage message, CancellationToken token = default )
    {
        lock ( __lock )
        {
            if ( !__rooms.TryGetValue(message.Room, out RoomLog? log) )
            {
                __rooms[message.Room] = log = new RoomLog();
            }

            (Guid, Guid) key = ( message.Sender.UserID, message.ClientID );
            if ( log.ByClientID.TryGetValue(key, out InstantMessage? existing) )
            {
                return ValueTask.FromResult(( existing, false ));
            }

            InstantMessage stored = message.WithSequence(++log.LastSequence);
            log.Messages.Add(stored);
            log.ByClientID[key] = stored;

            if ( log.Messages.Count > MaxMessagesPerRoom )
            {
                int remove = log.Messages.Count - MaxMessagesPerRoom;
                for ( int i = 0; i < remove; i++ )
                {
                    log.ByClientID.Remove(( log.Messages[i].Sender.UserID, log.Messages[i].ClientID ));
                }

                log.Messages.RemoveRange(0, remove);
            }

            return ValueTask.FromResult(( stored, true ));
        }
    }


    public ValueTask<InstantMessage[]> GetHistoryAsync( HistoryQuery query, CancellationToken token = default )
    {
        lock ( __lock )
        {
            if ( query.Count <= 0 || !__rooms.TryGetValue(query.Room, out RoomLog? log) || log.Messages.Count == 0 )
            {
                return ValueTask.FromResult(InstantMessage.Empty);
            }

            var messages = log.Messages; // ascending by Sequence
            int start;
            int end; // exclusive

            if ( query.After is { } after )
            {
                start = FirstAfter(messages, after);
                end   = Math.Min(messages.Count, start + query.Count);
            }
            else
            {
                end = query.Before is { } before
                          ? FirstAfter(messages, before - 1)
                          : messages.Count;

                start = Math.Max(0, end - query.Count);
            }

            return ValueTask.FromResult(end > start
                                            ? [.. messages.GetRange(start, end - start)]
                                            : InstantMessage.Empty);
        }
    }


    /// <summary> The index of the first message whose sequence is greater than <paramref name="sequence" />. </summary>
    private static int FirstAfter( List<InstantMessage> messages, long sequence )
    {
        int low  = 0;
        int high = messages.Count;

        while ( low < high )
        {
            int middle = low + ( ( high - low ) >> 1 );

            if ( messages[middle].Sequence <= sequence )
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }



    private sealed class RoomLog
    {
        public readonly Dictionary<(Guid UserID, Guid ClientID), InstantMessage> ByClientID = [];
        public readonly List<InstantMessage>                                     Messages   = [];
        public          long                                                     LastSequence;
    }
}
