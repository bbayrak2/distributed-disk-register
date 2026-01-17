using System.Net.NetworkInformation;
using Grpc.Net.Client;
using GrpcService.Services;
using GrpcService;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace GrpcService.Roles;

public class Member : IServer
{

    public async Task Run()
    {
        var port = 5556;
        while (!IsPortFree(port)) ++port;
        
        var builder = WebApplication.CreateBuilder();
        
        var config = new MemberConfig(port);
        builder.Services.AddSingleton(config);

    
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(port, o => { o.Protocols = HttpProtocols.Http2; });
        });
            
        
        builder.Services.AddGrpc();

        var app = builder.Build();

        app.MapGrpcService<FamilyService>();
        app.MapGrpcService<ChatService>();
        Console.WriteLine($"Member is running on {port}");


        app.Lifetime.ApplicationStarted.Register(() =>
        {
            Task.Run(async () => 
            {
                try 
                {
                    using var channel = GrpcChannel.ForAddress("http://localhost:5555");
                    var client = new Subscription.SubscriptionClient(channel);

                    var reply = await client.SubscribeAsync(new SubscribeRequest { Port = port });

                    Console.WriteLine($"Registration successful: {reply.Message}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not register to controller: {ex.Message}");
                }
            });
        });
        
        await app.RunAsync();
    }


    
    private bool IsPortFree(int port)
    {
        var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
        return tcpConnInfoArray.All(endpoint => endpoint.Port != port);
    }
}

public class MemberConfig(int port)
{
    public readonly int Port = port;
}