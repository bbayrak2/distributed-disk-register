using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Text.Json;
using System.IO;
using GrpcService.Services;

namespace GrpcService.Roles;

public class Leader(int backUp) : IServer
{
    private const int Port = 5555;
    private readonly Channel<TcpRequest> _channel = Channel.CreateUnbounded<TcpRequest>();
    private MemberRegistry _registry = new();
    private readonly Dictionary<int, Family.FamilyClient> _familyClients = new();
    private readonly Dictionary<int, Chat.ChatClient> _chatClients = new();
    private readonly HashSet<int> _availablePorts = new();
    private readonly object _lock = new();
    private readonly Dictionary<int, MemberInfo> _members = new();
    private string _membersFilePath = "members.json";
    private Dictionary<int, List<int>> _diskMap = [];


// Public property so other parts of your app can read the list safely
    public List<int> AvailablePorts 
    {
        get { lock(_lock) return _availablePorts.ToList(); }
    }
    

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
        var listener = new TcpListener(IPAddress.Any, 6666);
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
            Console.WriteLine(msg);
            await _channel.Writer.WriteAsync(new TcpRequest(client,msg));
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
                    await HandleGetRequest(item.Client,id);
                    break;
                case "SET":
                    await HandleSetRequest(item.Client,id,parts[2],backUp);
                    break;
            }
            
        }
    }
    
    
    private async Task HandleGetRequest(TcpClient client, int id)
    {
        await using var stream = client.GetStream();
        string? response;
        byte[]? responseBytes;
        if (!_diskMap.ContainsKey(id))
        {
            response = $"ERROR, {id} message is unreachable";
            responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
            client.Close();
            throw new Exception("Message is unreachable");
        }
        var ports = _diskMap[id];
        
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
                var text = _chatClients[port].GetMessage(request).Text;
                response = $"OK, {id} {text}";
                responseBytes = Encoding.UTF8.GetBytes(response);
                await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
                client.Close();
                return;
            }
            catch(Exception ex)
            {
                Console.WriteLine($"GET Error on port:{port}");
            }
        }

        response = $"ERROR, {id}";
        responseBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
        client.Close();
        throw new Exception("Message is unreachable");
    }


    private async Task HandleSetRequest(TcpClient client,int id, string text, int backUpNumber)
    {
        await using var stream = client.GetStream();
        string response;
        byte[] responseBytes;
        if (_diskMap.ContainsKey(id))
        {
            response = $"ERROR, {id} There is a message with same id";
            responseBytes = Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
            client.Close();
            throw new Exception("Message is unreachable");
        }
        var backUpCounter = 0;
        var orderedPortList = _members
            .Where(kv => kv.Value.Connected)
            .OrderBy(kv => kv.Value.MessageNumber)
            .Select(kv => kv.Key)
            .ToList();
        
        lock(_lock) {
            _diskMap[id] = new List<int>();
        }

        foreach (var port in orderedPortList.TakeWhile(port => backUpCounter != backUp))
        {
            if (backUpCounter == backUpNumber)
            {
                break;
            }

            
            try
            {
                var grpcResponse = _chatClients[port].SetMessage(new SetRequest
                {
                    Id = id,
                    Text = text,
                    FromHost = "localhost",
                    FromPort = Port,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });
                lock(_lock) { _diskMap[id].Add(port); }
                ++backUpCounter;
                _members[port].MessageNumber += 1;
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC Exception: {ex}");
            }
        }

        response = backUpCounter >= backUpNumber ? "OK" : "ERROR: Not enough backups";
        responseBytes = Encoding.UTF8.GetBytes(response);
        await stream.WriteAsync(responseBytes, 0, responseBytes.Length);
    
    }

    private async Task CheckFamily()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        
        while (await timer.WaitForNextTickAsync())
        {

            Console.WriteLine($"\n--- Family Check at {DateTime.Now:HH:mm:ss} ---");
            Console.WriteLine("=====================================");

            var subs = _registry.Ports.ToHashSet();
            

            foreach (var port in subs)
            {
                if (!_members.ContainsKey(port))
                {
                    _members.Add(port,new MemberInfo(0,false));
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
                    lock(_lock) _availablePorts.Add(port);
                }
                catch (RpcException)
                {
                    lock(_lock)
                    {
                        if (_availablePorts.Remove(port))
                            Console.WriteLine($"Port {port} removed (unreachable).");
                    }
                }

            }

            foreach (var member in _members)
            {
                member.Value.Connected = _availablePorts.Contains(member.Key);
            }

            
            
            if (_availablePorts.Count == 0)
            {
                Console.WriteLine("Empty");
                continue;
            }
            
            foreach (var port in _availablePorts)
            {
                _members[port].Connected = true;
                _familyClients[port].SendFamily(new FamilyMembers{Ports = { _availablePorts }});
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

public class TcpRequest(TcpClient client, string text)
{
    public TcpClient Client = client;
    public string Text = text;
}
