using Grpc.Core;
using GrpcService.Roles;
using GrpcService;
using Microsoft.Extensions.Primitives;

namespace GrpcService.Services;

public class ChatService(MemberConfig config) : Chat.ChatBase
{
    private readonly int _port = config.Port;

    public override Task<SetResponse> SetMessage(SetRequest request, ServerCallContext context)
    {
        try
        {
            var baseDirectory = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName;
            var path = Path.Combine(baseDirectory, "Records", _port.ToString());
            Directory.CreateDirectory(path);

            path = Path.Combine(path, $"{request.Id.ToString()}.txt");
            var lines = new[]
                { request.Text, request.FromHost, request.FromPort.ToString(), request.Timestamp.ToString() };
            File.WriteAllLines(path, lines);
            return Task.FromResult(new SetResponse
            {
                Id = request.Id,
                Text = request.Text,
                Port = _port
            });
        }
        catch (Exception ex)
        {
            throw new RpcException(new Status(StatusCode.Cancelled,"Member cannot save the message"));
        }
        
    }
    
    
    public override Task<GetResponse> GetMessage(GetRequest request, ServerCallContext context)
    {
        try
        {
            var baseDirectory = Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName;
            var fileName = request.Id.ToString() + ".txt";
            var path = Path.Combine(baseDirectory, "Records", _port.ToString(),fileName);
            var content = File.ReadAllText(path);
            
            return Task.FromResult(new GetResponse
            {
                Id = request.Id,
                Text = content
            });

        }
        catch (Exception ex)
        {
            throw ex;
        }
    }
}
    
