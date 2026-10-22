// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

namespace Jakar.SignalR.Chats;


public sealed class ChatServerOptions
{
    /// <summary>
    ///     When the connection isn't authenticated, trust the <see cref="ChatUser" /> the client logs in as. Off by
    ///     default: turn it on only for development or trusted networks.
    /// </summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>
    ///     Send <see cref="IChatClient.LoggedIn" /> / <see cref="IChatClient.LoggedOut" /> to every connected client.
    ///     Default <see langword="true" />.
    /// </summary>
    public bool BroadcastPresence { get; set; } = true;

    /// <summary>
    ///     The most messages one <see cref="IChatHub.History" /> call (or <see cref="IChatHub.JoinRoom" />'s latest
    ///     messages) returns. Default 200.
    /// </summary>
    public int MaxHistoryCount { get; set; } = 200;

    /// <summary>
    ///     The number of latest messages <see cref="IChatHub.JoinRoom" /> returns. Default
    ///     <see cref="HistoryQuery.DEFAULT_COUNT" />.
    /// </summary>
    public int JoinHistoryCount { get; set; } = HistoryQuery.DEFAULT_COUNT;

    /// <summary> The most attachments a message may carry. Default 10. </summary>
    public int MaxAttachments { get; set; } = 10;

    /// <summary>
    ///     The most payload characters a message's attachments may carry together (binary files are base64: about 4/3 of
    ///     their size). Default 4 MiB.
    /// </summary>
    public long MaxAttachmentsLength { get; set; } = 4 * 1024 * 1024;

    /// <summary> The longest message text. Default <see cref="Constants.MAX_FIXED" />. </summary>
    public int MaxTextLength { get; set; } = MAX_FIXED;

    /// <summary> The longest room name. Default <see cref="Constants.NAME" />. </summary>
    public int MaxRoomNameLength { get; set; } = NAME;

    /// <summary>
    ///     SignalR's <see cref="HubOptions.MaximumReceiveMessageSize" /> for the chat hub: the largest frame a client may
    ///     send. SignalR's own default (32 KiB) is too small for attachments,
    ///     so the default is <see cref="MaxAttachmentsLength" /> plus 64 KiB for the text and the envelope; set it yourself
    ///     when you change that.
    /// </summary>
    public long? MaximumReceiveMessageSize { get; set; }


    internal long GetMaximumReceiveMessageSize() => MaximumReceiveMessageSize ?? MaxAttachmentsLength + 64 * 1024;
}
