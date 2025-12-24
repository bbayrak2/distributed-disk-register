using Grpc.Core;
using System.Collections.Concurrent;

namespace GrpcService.Services;

public class SubscriptionService : Subscription.SubscriptionBase
{
    private readonly MemberRegistry _registry;

    public SubscriptionService(MemberRegistry registry) // Injected via DI
    {
        _registry = registry;
    }

    public override Task<SubscribeResponse> Subscribe(SubscribeRequest request, ServerCallContext context)
    {
        _registry.Ports.Add(request.Port);
        return Task.FromResult(new SubscribeResponse { Message = "Added" });
    }
}

public class MemberRegistry
{
    public ConcurrentBag<int> Ports { get; } = new();
}