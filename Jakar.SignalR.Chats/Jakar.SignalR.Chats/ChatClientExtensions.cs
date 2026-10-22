// Jakar.SignalR.Chats :: Jakar.SignalR.Chats
// 10/21/2026

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;



namespace Jakar.SignalR.Chats;


public static class ChatClientExtensions
{
    /// <summary>
    ///     Registers <see cref="ChatClient" /> as a singleton and as a hosted service, so the host connects it on start
    ///     and disconnects it on stop.
    /// </summary>
    public static IServiceCollection AddChatClient( this IServiceCollection services, Action<ChatClientOptions> configure )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<ChatClientOptions>().Configure(configure);
        services.TryAddSingleton(static provider => new ChatClient(provider.GetRequiredService<IOptions<ChatClientOptions>>(), provider.GetService<ILogger<ChatClient>>()));
        services.AddHostedService(static provider => provider.GetRequiredService<ChatClient>());
        return services;
    }
}
