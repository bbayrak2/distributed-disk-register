using Grpc.Core;
using GrpcService.Roles;

namespace GrpcService.Services;

public class ChatService : Chat.ChatBase
{
    private int _port;
    private string baseDirectory = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName;
    private string path;

    public ChatService(MemberConfig config)
    {
        _port =  config.Port;
        path = Path.Combine(baseDirectory, "Records", _port.ToString());
        Directory.CreateDirectory(path);
    }
    
    public override async Task<SetResponse> SetMessage(SetRequest request, ServerCallContext context)
    {
        try
        {
            var filePath = Path.Combine(path, $"{request.Id}.txt");
            var lines = new[]
                { request.Text, request.FromHost, request.FromPort.ToString(), request.Timestamp.ToString() };
            
            await File.WriteAllLinesAsync(filePath, lines);
            
            return new SetResponse
            {
                Id = request.Id,
                Text = request.Text,
                Port = _port
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine("Member cannot save the message : " );
            throw new RpcException(new Status(StatusCode.Cancelled,"Member cannot save the message"));
        }
    }
    
    public override async Task<GetResponse> GetMessage(GetRequest request, ServerCallContext context)
    {
        try
        {
            var fileName = request.Id + ".txt";
            var filePath = Path.Combine(baseDirectory, "Records", _port.ToString(),fileName);
            
            if (!File.Exists(filePath))
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"Message {request.Id} not found"));
            }
            var lines = await File.ReadAllLinesAsync(filePath);
            
            var content = lines.Length > 0 ? lines[0] : string.Empty;
            
            return new GetResponse
            {
                Id = request.Id,
                Text = content
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine("Member cannot reach the message");
            throw; 
        }
    }
}
    
