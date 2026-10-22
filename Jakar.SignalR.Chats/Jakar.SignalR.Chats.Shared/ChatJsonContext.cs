// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;



namespace Jakar.SignalR.Chats;


/// <summary>
///     Source-generated System.Text.Json metadata for the chat models (what the hub protocol needs, also under
///     Native AOT).
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ChatUser))]
[JsonSerializable(typeof(ChatUser[]))]
[JsonSerializable(typeof(ChatRoomState))]
[JsonSerializable(typeof(ChatRoomState[]))]
[JsonSerializable(typeof(FileData))]
[JsonSerializable(typeof(FileData[]))]
[JsonSerializable(typeof(HistoryQuery))]
[JsonSerializable(typeof(HubEvent))]
[JsonSerializable(typeof(InstantMessage))]
[JsonSerializable(typeof(InstantMessage[]))]
[JsonSerializable(typeof(string[]))]
public sealed partial class ChatJsonContext : JsonSerializerContext;



public static class ChatProtocol
{
    /// <summary>
    ///     The hub protocol both sides of the chat use: SignalR's JSON protocol, with the chat models' metadata from
    ///     <see cref="ChatJsonContext" /> (no reflection).
    ///     Call it on the server's <c>AddSignalR()</c> and on the client's <c>HubConnectionBuilder</c> (the chat packages do).
    /// </summary>
    /// <remarks>
    ///     TODO: switch to Jakar.Json.SignalR's <c>AddJakarJsonProtocol</c> once it's republished against Jakar.Json 10.2.0
    ///     (Jakar.Json.SystemTextJson 10.1.0 calls a <c>JsonTypeCodec.Write</c> overload that 10.2.0 removed, and fails with
    ///     <see cref="MissingMethodException" />).
    /// </remarks>
    public static TBuilder AddChatProtocol<TBuilder>( this TBuilder builder, Action<JsonHubProtocolOptions>? configure = null )
        where TBuilder : ISignalRBuilder
    {
        return builder.AddJsonProtocol(options =>
                                       {
                                           options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, ChatJsonContext.Default);
                                           configure?.Invoke(options);
                                       });
    }
}
