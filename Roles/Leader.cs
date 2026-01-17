using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using GrpcService.Services;
using GrpcService;

namespace GrpcService.Roles;

public class Leader(int backUp) : IServer
{
    private const int Port = 5555;
    private const int TcpPort = 6666;
    
    private readonly Channel<TcpRequest> _channel = Channel.CreateUnbounded<TcpRequest>();
    private readonly MemberRegistry _registry = new();
    private readonly ConcurrentDictionary<int, Family.FamilyClient> _familyClients = new();
    private readonly ConcurrentDictionary<int, Chat.ChatClient> _chatClients = new();
    private readonly ConcurrentDictionary<int, MemberInfo> _members = new();
    private readonly ConcurrentDictionary<int, List<int>> _diskMap = [];

    private readonly ConcurrentDictionary<int,byte> _availablePorts = [];
    private IEnumerable<int> AvailablePorts => _availablePorts.Keys;
    
    

    public async Task Run()
    {
        await Task.WhenAll(
            ListenClientsAsync(),
            ListenMemberAsync(),
            ProcessChannelAsync(),
            CheckFamily()
        );
    }
    
    
    private async Task ListenClientsAsync()
    {
        var listener = new TcpListener(IPAddress.Any, TcpPort);
        listener.Start();
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }
    private async Task HandleClientAsync(TcpClient client)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync() is { } msg)
        {
            if (string.IsNullOrWhiteSpace(msg)) continue;
            await _channel.Writer.WriteAsync(new TcpRequest(stream,msg));
        }
    }
    private async Task ListenMemberAsync()  //gRPC
    {
        
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_registry);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ListenAnyIP(Port, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });
        builder.Services.AddGrpc();

        var app = builder.Build();
        app.MapGrpcService<SubscriptionService>();

        Console.WriteLine($"Leader is running on {Port}");
        await app.RunAsync();
    }

    private async Task ProcessChannelAsync()
    {
        
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            var text = item.Text;
            var parts = text.Split(' ');
            var id = Convert.ToInt32(parts[1]);
            switch (parts[0])
            {
                case "GET":
                    await HandleGetRequest(item.Stream,id);
                    break;
                case "SET":
                    await HandleSetRequest(item.Stream,id,parts[2],backUp);
                    break;
            }
            
        }
    }
    
    
    private async Task HandleGetRequest(NetworkStream stream, int id)
    {
        string response;
        if (!_diskMap.TryGetValue(id, out var ports))
        {
            response = $"ERROR, {id} message is unreachable\n"; 
            await SendResponseAsync(stream, response);
            return;
        }

        var dataFound = false;
        foreach (var port in ports)
        {
            try
            {
                var request = new GetRequest
                {
                    Id = id,
                    FromHost = "localhost",
                    FromPort = Port,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                var rpcResponse = await _chatClients[port].GetMessageAsync(request); 
                var text = rpcResponse.Text;

                response = $"{id} {text} OK\n"; 
                await SendResponseAsync(stream, response);

                dataFound = true;
                return; 
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Warning] GET Error on port {port} for ID {id}: {ex.Message}");
            }
        }
        if (!dataFound)
        {
            Console.WriteLine($"[Error] Failed to retrieve ID {id} from all replicas: {string.Join(",", ports)}");
            response = $"ERROR, {id} not found on any node\n";
            await SendResponseAsync(stream, response);
        }
    }

    private async Task HandleSetRequest(NetworkStream stream,int id, string text, int backUpNumber)
    {
        string response;
        if (_diskMap.TryGetValue(id, out _))
        {
            response = $"ERROR, {id} There is a message with same id";
            await SendResponseAsync(stream, response);
            return;
        }
        var backUpCounter = 0;
        var orderedPortList = _members
                .Where(kv => kv.Value.Connected)
                .OrderBy(kv => kv.Value.MessageNumber)
                .Select(kv => kv.Key)
                .ToList();
            _diskMap[id] = [];

        foreach (var port in orderedPortList.TakeWhile(_ => backUpCounter != backUp))
        {
            if (backUpCounter == backUpNumber)
            {
                break;
            }

            
            try
            {
                _chatClients[port].SetMessage(new SetRequest
                {
                    Id = id,
                    Text = text,
                    FromHost = "localhost",
                    FromPort = Port,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                _diskMap[id].Add(port);
                ++backUpCounter;
                _members[port].MessageNumber += 1;
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC Exception: {ex}");
            }
        }

        response = backUpCounter >= backUpNumber ? "OK" : "ERROR: Not enough backups";
        await SendResponseAsync(stream, response +"\n");
    }

    private static async Task SendResponseAsync(NetworkStream stream, string message)
    {
        var responseBytes = Encoding.UTF8.GetBytes(message);
        await stream.WriteAsync(responseBytes);
    }
    private async Task CheckFamily()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        
        while (await timer.WaitForNextTickAsync())
        {

            Console.WriteLine($"\n--- Family Check at {DateTime.Now:HH:mm:ss} ---");
            Console.WriteLine("=====================================");

            var subs = _registry.Ports.ToHashSet();
            

            foreach (var port in subs)
            {
                if (!_members.TryGetValue(port, out _))
                {
                    _members[port] = new MemberInfo(0, false);
                }
                try
                {
                    var channel = GrpcChannel.ForAddress($"http://localhost:{port}");
                    if (!_familyClients.TryGetValue(port, out var familyClient))
                    {
                        familyClient = new Family.FamilyClient(channel);
                        _familyClients[port] = new Family.FamilyClient(channel);
                        _chatClients[port] = new Chat.ChatClient(channel);
                    }
                    

                    

                    var reply = await familyClient.FamilyCheckAsync(
                        new FamilyRequest{Port = port},deadline: DateTime.UtcNow.AddSeconds(1));


                    if (!reply.Active) continue;

                    _availablePorts.TryAdd(port,0);
                        
                }
                catch (RpcException)
                {

                    _availablePorts.TryRemove(port,out _);
                    
                    Console.WriteLine($"Port {port} removed (unreachable).");
                
                }

            }

            foreach (var member in _members)
            {
                member.Value.Connected = AvailablePorts.Contains(member.Key);
            }

            
            
            if (_availablePorts.IsEmpty)
            {
                Console.WriteLine("Empty");
                continue;
            }
            
            foreach (var port in AvailablePorts)
            {
                _members[port].Connected = true;
                _familyClients[port].SendFamily(new FamilyMembers{Ports = { AvailablePorts }});
            }
            
            Console.WriteLine($"Message number: {_diskMap.Keys.Count}");
            foreach (var connectedPorts in _members
                         .Where(kv => kv.Value.Connected).ToList())
            {
                var keyPort = connectedPorts.Key;
                var messageNumber = _diskMap.Count(x => x.Value.Contains(keyPort));
                Console.WriteLine($"{keyPort} : saved {messageNumber } message");   
            }
            
            
            Console.WriteLine("=====================================");
        }
    }



    
    
    
}


public class MemberInfo(int number, bool connected)
{
    public int MessageNumber = number;
    public bool Connected = connected;
}

public class TcpRequest(NetworkStream stream, string text)
{
    public NetworkStream Stream = stream;
    public readonly string Text = text;
}
