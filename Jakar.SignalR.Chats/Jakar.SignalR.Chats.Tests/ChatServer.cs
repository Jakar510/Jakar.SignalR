// Jakar.SignalR.Chats.Tests
// 10/21/2026

using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;



namespace Jakar.SignalR.Chats.Tests;


/// <summary>
///     A real Kestrel chat server on a loopback port. The store is kept outside the app, so a restarted server (same
///     port) still has the history.
/// </summary>
public sealed class ChatServer : IAsyncDisposable
{
    private WebApplication? __app;


    private ChatServer( int port )
    {
        __Port    = port;
        Address = new Uri($"http://127.0.0.1:{port}/");
    }


    public  Uri               Address { get; }
    public  InMemoryChatStore Store   { get; } = new();
    private int               __Port    { get; }


    public ValueTask DisposeAsync() => new(StopAsync());


    public static async Task<ChatServer> StartAsync()
    {
        ChatServer server = new(GetFreePort());
        await server.StartAgainAsync();
        return server;
    }


    public async Task StartAgainAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls(Address.ToString());
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IChatStore>(Store);
        builder.Services.AddChatServer(static options => options.AllowAnonymous = true);

        WebApplication app = builder.Build();
        app.UseWebSockets();
        app.MapChatHub();
        await app.StartAsync();
        __app = app;
    }


    public async Task StopAsync()
    {
        if ( __app is null )
        {
            return;
        }

        await __app.StopAsync();
        await __app.DisposeAsync();
        __app = null;
    }


    public ChatClient CreateClient() =>
        new(new ChatClientOptions
            {
                HostInfo      = Address,
                RetryDelays   = [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250)],
                TypingTimeout = TimeSpan.FromSeconds(2)
            });


    private static int GetFreePort()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return ( (IPEndPoint)listener.LocalEndpoint ).Port;
    }
}



internal static class Wait
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);


    public static async Task UntilAsync( Func<bool> condition, string because )
    {
        DateTime end = DateTime.UtcNow + Timeout;

        while ( !condition() )
        {
            if ( DateTime.UtcNow > end )
            {
                Assert.Fail($"Timed out waiting until {because}.");
            }

            await Task.Delay(20);
        }
    }
}
