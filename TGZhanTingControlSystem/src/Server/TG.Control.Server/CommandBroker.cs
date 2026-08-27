using System.Collections.Concurrent;
using System.Threading.Channels;
using TG.Control.Contracts;

namespace TG.Control.Server;

public interface ICommandBroker
{
    void Register(ClientRegistration registration);
    IReadOnlyList<ClientRuntimeStatus> GetClientStatuses(TimeSpan onlineThreshold);
    long NextSequence();
    ValueTask PublishAsync(string clientId, PlaybackCommand command, CancellationToken cancellationToken);
    Task<PlaybackCommand?> WaitAsync(string clientId, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class CommandBroker : ICommandBroker
{
    private readonly ConcurrentDictionary<string, Channel<PlaybackCommand>> channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ClientPresence> clients = new(StringComparer.OrdinalIgnoreCase);
    private long sequence;

    public void Register(ClientRegistration registration)
    {
        var now = DateTimeOffset.UtcNow;
        clients.AddOrUpdate(registration.ClientId,
            _ => new ClientPresence(registration.ClientId, registration.Kind, registration.AppVersion, now, now,
                registration.ContentVersion, registration.Ready, registration.Status),
            (_, current) => current with
            {
                Kind = registration.Kind,
                AppVersion = registration.AppVersion,
                LastSeenUtc = now,
                ContentVersion = registration.ContentVersion,
                Ready = registration.Ready,
                Status = registration.Status
            });
        GetChannel(registration.ClientId);
    }

    public IReadOnlyList<ClientRuntimeStatus> GetClientStatuses(TimeSpan onlineThreshold)
    {
        var onlineAfter = DateTimeOffset.UtcNow - onlineThreshold;
        return clients.Values
            .OrderBy(client => client.ClientId, StringComparer.OrdinalIgnoreCase)
            .Select(client => new ClientRuntimeStatus(client.ClientId, client.Kind, client.AppVersion,
                client.RegisteredAtUtc, client.LastSeenUtc, client.LastSeenUtc >= onlineAfter,
                client.ContentVersion, client.Ready, client.Status))
            .ToArray();
    }

    public long NextSequence() => Interlocked.Increment(ref sequence);

    public ValueTask PublishAsync(string clientId, PlaybackCommand command, CancellationToken cancellationToken) =>
        GetChannel(clientId).Writer.WriteAsync(command, cancellationToken);

    public async Task<PlaybackCommand?> WaitAsync(string clientId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Touch(clientId);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var command = await GetChannel(clientId).Reader.ReadAsync(timeoutSource.Token);
            Touch(clientId);
            return command;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Touch(clientId);
            return null;
        }
    }

    private void Touch(string clientId)
    {
        if (clients.TryGetValue(clientId, out var current))
        {
            clients.TryUpdate(clientId, current with { LastSeenUtc = DateTimeOffset.UtcNow }, current);
        }
    }

    private Channel<PlaybackCommand> GetChannel(string clientId) => channels.GetOrAdd(clientId, _ =>
        Channel.CreateUnbounded<PlaybackCommand>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }));

    private sealed record ClientPresence(string ClientId, ClientKind Kind, string AppVersion,
        DateTimeOffset RegisteredAtUtc, DateTimeOffset LastSeenUtc, long ContentVersion, bool Ready, string? Status);
}
