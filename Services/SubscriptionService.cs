using Grpc.Core;
using System.Collections.Concurrent;
using GrpcService;

namespace GrpcService.Services;

public class SubscriptionService(MemberRegistry registry) : Subscription.SubscriptionBase
{

    public override Task<SubscribeResponse> Subscribe(SubscribeRequest request, ServerCallContext context)
    {
        registry.Ports.Add(request.Port);
        return Task.FromResult(new SubscribeResponse { Message = "Added" });
    }
}

public class MemberRegistry
{
    public ConcurrentBag<int> Ports { get; } = new();
}