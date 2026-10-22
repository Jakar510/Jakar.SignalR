// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;



namespace Jakar.SignalR.Chats;


public static class ChatServerExtensions
{
    extension( IServiceCollection services )
    {
        /// <summary> <see cref="AddChatServer{THub}" /> for <see cref="ChatHub" /> itself. </summary>
        public ISignalRServerBuilder AddChatServer( Action<ChatServerOptions>? configure = null ) => services.AddChatServer<ChatHub>(configure);


        /// <summary>
        ///     Adds SignalR with the chat protocol (<see cref="ChatProtocol.AddChatProtocol{TBuilder}" />),
        ///     <see cref="ChatServerOptions" />, and the in-memory <see cref="IChatPresence" /> and <see cref="IChatStore" />
        ///     unless you registered your own first. Sets the hub's <see cref="HubOptions.MaximumReceiveMessageSize" /> from
        ///     <see cref="ChatServerOptions.MaximumReceiveMessageSize" />, so attachments fit.
        /// </summary>
        public ISignalRServerBuilder AddChatServer<THub>( Action<ChatServerOptions>? configure = null )
            where THub : ChatHub
        {
            ArgumentNullException.ThrowIfNull(services);

            var options = services.AddOptions<ChatServerOptions>();
            if ( configure is not null ) { options.Configure(configure); }

            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IChatPresence, InMemoryChatPresence>();
            services.TryAddSingleton<IChatStore, InMemoryChatStore>();

            ISignalRServerBuilder builder = services.AddSignalR().AddChatProtocol();
            services.AddOptions<HubOptions<THub>>().Configure<IOptions<ChatServerOptions>>(static ( hub, chat ) => hub.MaximumReceiveMessageSize = chat.Value.GetMaximumReceiveMessageSize());

            return builder;
        }
    }



    extension( IEndpointRouteBuilder endpoints )
    {
        /// <summary> Maps <typeparamref name="THub" /> at <paramref name="pattern" /> (<see cref="IChatHub.PATH" /> by default). </summary>
        public HubEndpointConventionBuilder MapChatHub<THub>( [StringSyntax("Route")] string pattern = IChatHub.PATH )
            where THub : ChatHub =>
            endpoints.MapHub<THub>(pattern);
        /// <summary> Maps <see cref="ChatHub" /> at <paramref name="pattern" /> (<see cref="IChatHub.PATH" /> by default). </summary>
        public HubEndpointConventionBuilder MapChatHub( [StringSyntax("Route")] string pattern = IChatHub.PATH ) => endpoints.MapChatHub<ChatHub>(pattern);
    }
}
