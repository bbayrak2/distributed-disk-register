using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc;
using GrpcService.Roles;
using GrpcService;

namespace GrpcService.Services;

public class FamilyService(MemberConfig config) : Family.FamilyBase
{
    private readonly int _port = config.Port;
    public override Task<FamilyResponse> FamilyCheck(FamilyRequest request, ServerCallContext context)
    {
        return Task.FromResult(new FamilyResponse { Port = request.Port, Active = true});
    }
    
    public override Task<Empty> SendFamily(FamilyMembers request, ServerCallContext context)
    {
        Console.WriteLine("Family:");
        foreach (var port in request.Ports)
        {
            if (port == _port)
            {
                Console.WriteLine(port + " (me)");
                continue;
            }
            Console.WriteLine(port);

        }
        return Task.FromResult(new Empty());
    }
}
