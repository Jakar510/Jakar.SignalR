// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

using Microsoft.AspNetCore.Http.Connections.Client;



namespace Jakar.SignalR.Chats;


[JsonSerializable(typeof(ChatRoom))]
[JsonSerializable(typeof(ChatRoomCollection))]
[JsonSerializable(typeof(ChatUserCollection))]
[JsonSerializable(typeof(InstantMessageCollection))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
public sealed partial class ChatClientJsonContext : JsonSerializerContext;



public sealed class ChatClientOptions
{
    /// <summary> The server, e.g. <c>https://chat.example.com</c>. Required. </summary>
    public Uri? HostInfo { get; set; }

    /// <summary>
    ///     The hub's path on <see cref="HostInfo" /> (<see cref="IChatHub.PATH" /> by default). An absolute path
    ///     replaces any path in <see cref="HostInfo" />.
    /// </summary>
    public string Path { get; set; } = IChatHub.PATH;

    /// <summary> Supplies the bearer token for every connection (and reconnection). </summary>
    public Func<Task<string?>>? AccessTokenProvider { get; set; }

    /// <summary> Further transport settings (headers, transports, an <see cref="HttpMessageHandler" />, ...). </summary>
    public Action<HttpConnectionOptions>? ConfigureHttpConnection { get; set; }

    /// <summary> Further connection settings (logging, keep-alive, ...) before it's built. </summary>
    public Action<IHubConnectionBuilder>? ConfigureConnection { get; set; }

    /// <summary> The waits between attempts to connect or reconnect; the last one repeats for as long as it takes. </summary>
    public TimeSpan[] RetryDelays { get; set; } = [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>
    ///     How long someone shows as typing after their last <see cref="IChatClient.Typing" />; also the client sends
    ///     <see cref="IChatHub.Typing" /> at most every half of it. Default 5 seconds.
    /// </summary>
    public TimeSpan TypingTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     Messages per <see cref="IChatHub.History" /> call when catching up or scrolling back. Default
    ///     <see cref="HistoryQuery.DEFAULT_COUNT" />.
    /// </summary>
    public int HistoryPageSize { get; set; } = HistoryQuery.DEFAULT_COUNT;

    public TimeProvider Clock { get; set; } = TimeProvider.System;


    public Uri GetHubUri() => new(HostInfo ?? throw new InvalidOperationException($"{nameof(ChatClientOptions)}.{nameof(HostInfo)} is required."), Path);

    public TimeSpan GetRetryDelay( long attempt ) =>
        RetryDelays.Length == 0
            ? TimeSpan.FromSeconds(5)
            : RetryDelays[(int)Math.Min(attempt, RetryDelays.Length - 1)];
}
