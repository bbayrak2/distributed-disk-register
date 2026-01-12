using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Concurrent;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using GrpcService.Services;

namespace GrpcService.Roles;

public class Leader(int backUp) : IServer
{
    private const int Port = 5555;
    private const int TcpPort = 6666;
    private readonly MemberRegistry _registry = new();
    private readonly ConcurrentDictionary<int, Family.FamilyClient> _familyClients = new();
    private readonly ConcurrentDictionary<int, Chat.ChatClient> _chatClients = new();
    private readonly ConcurrentDictionary<int, MemberInfo> _members = new();
    private readonly ConcurrentDictionary<int, List<int>> _diskMap = [];
    private readonly ConcurrentDictionary<int, byte> _availablePorts = new();

    private IEnumerable<int> AvailablePorts => _availablePorts.Keys;

    public async Task Run()
    {
        await Task.WhenAll(
            ListenClientsAsync(),
            ListenMemberAsync(),
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
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var writer = new StreamWriter(stream, Encoding.UTF8){AutoFlush = true};
            
            while (await reader.ReadLineAsync() is { } msg)
            {
                if (string.IsNullOrWhiteSpace(msg)) continue;
                
                
                var parts = msg.Split(' ', 3);
                if (parts.Length < 2)
                {
                    await writer.WriteLineAsync("ERROR Invalid Protocol");
                    continue;
                }

                var cmd = parts[0].ToUpper();
                if (!int.TryParse(parts[1], out var id))
                {
                    await writer.WriteLineAsync("ERROR Invalid ID Format");
                    continue;
                }
                switch (cmd)
                {
                    case "GET":
                        await HandleGetRequest(writer, id);
                        break;
                    
                    case "SET":
                        if (parts.Length < 3)
                        {
                            await writer.WriteLineAsync("ERROR Invalid Protocol");
                        }
                        else
                        {
                            await HandleSetRequest(writer, id, parts[2], backUp);
                        }
                        break;
                    
                    default:
                        await writer.WriteLineAsync($"ERROR Unknown Command {cmd}");
                        break;
                }
            }
        }catch (Exception ex)
        {
            Console.WriteLine($"[Client Error] {ex.Message}");
        }
        finally
        {
            client.Close();
        }
    }

    private async Task ListenMemberAsync()  //gRPC
    {
        
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(_registry);
        builder.Logging.ClearProviders();
        
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

    
    
    
    private async Task HandleGetRequest(StreamWriter writer, int id)
    {
        if (!_diskMap.TryGetValue(id, out var ports))
        {
            await writer.WriteLineAsync($"ERROR, {id} message is unreachable\n");
            return;
        }

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

                
                await writer.WriteLineAsync($"OK, {id} {text} \n");
                return;
            }
            catch(Exception ex)
            {
                Console.WriteLine($"[Warning] GET Error on port {port} for ID {id}: {ex.Message}");
            }
        }
        Console.WriteLine($"[Error] Failed to retrieve ID {id} from all replicas: {string.Join(",", ports)}");
        await writer.WriteLineAsync($"ERROR Key {id} exists in map but missing on nodes");
        
    }


    private async Task HandleSetRequest(StreamWriter writer,int id, string text, int backUpNumber)
    {
        if (_diskMap.ContainsKey(id))
        {
            await writer.WriteLineAsync($"ERROR, {id} There is a message with same id\n");
            return;
        }
        var backUpCounter = 0;
        
        var orderedPortList = _members
                .Where(kv => kv.Value.Connected)
                .OrderBy(kv => kv.Value.MessageNumber)
                .Select(kv => kv.Key)
                .ToList();

        _diskMap.TryAdd(id, []);

        foreach (var port in orderedPortList)
        {
            if (backUpCounter == backUpNumber) break;
            try
            {
                
                await _chatClients[port].SetMessageAsync(new SetRequest
                {
                    Id = id,
                    Text = text,
                    FromHost = "localhost",
                    FromPort = Port,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                });

                lock (_diskMap[id])
                {
                    _diskMap[id].Add(port);
                }
                    
                ++backUpCounter;
                _members[port].MessageNumber += 1;
            }
            catch (RpcException ex)
            {
                Console.WriteLine($"RPC Exception: {ex}");
                _diskMap.TryRemove(id, out _);
            }
        }

        if (backUpCounter >= backUpNumber)
        {
            await writer.WriteLineAsync("OK");
        }
        else
        {
            _diskMap.TryRemove(id, out _);
            await writer.WriteLineAsync("ERROR: Not enough backups");
        }
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
                    _members.TryAdd(port, new MemberInfo(0, false));
                }
                try
                {
                    if (!_familyClients.ContainsKey(port))
                    {
                        var channel = GrpcChannel.ForAddress($"http://localhost:{port}");
                        var newFamilyClient = new Family.FamilyClient(channel);
                        var newChatClient = new Chat.ChatClient(channel);
                        _familyClients.TryAdd(port, newFamilyClient);
                        _chatClients.TryAdd(port, newChatClient);
                    }
                    
                    if (_familyClients.TryGetValue(port, out var familyClient))
                    {
                        var reply = await familyClient.FamilyCheckAsync(
                            new FamilyRequest{Port = port},deadline: DateTime.UtcNow.AddSeconds(1));
                        
                        if (reply.Active)
                        {
                            _availablePorts.TryAdd(port, 0);
                        }
                    }
                }
                catch (RpcException)
                {
                    if(AvailablePorts.Contains(port))
                    {
                        _availablePorts.TryRemove(port, out _);
                        Console.WriteLine($"Port {port} removed (unreachable).");
                    }
                
                }

            }

            foreach (var member in _members)
            {
                member.Value.Connected = _availablePorts.ContainsKey(member.Key);
            }

            
            
            if (_availablePorts.IsEmpty)
            {
                Console.WriteLine("Empty");
                continue;
            }
            
            foreach (var port in _availablePorts.Keys)
            {
                if (_members.TryGetValue(port, out var member)) member.Connected = true;
                
                if(_familyClients.TryGetValue(port, out var client))
                {
                     await client.SendFamilyAsync(new FamilyMembers{Ports = { _availablePorts.Keys }});
                }
            }
            
            Console.WriteLine($"Message number: {_diskMap.Keys.Count}");
            foreach (var connectedPorts in _members
                         .Where(kv => kv.Value.Connected).ToList())
            {
                var keyPort = connectedPorts.Key;
                var messageNumber = _diskMap.Values.Count(x => x.Contains(keyPort));
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
