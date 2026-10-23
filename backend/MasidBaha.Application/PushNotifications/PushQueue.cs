using System.Threading.Channels;
using MasidBaha.Application.Common.Options;
using Microsoft.Extensions.Options;

namespace MasidBaha.Application.PushNotifications;

// Hand-off between the request that created a report and the background
// worker that sends the notifications. Sending to every subscriber used to
// happen inside the HTTP request and made the reporter wait for it.
public interface IPushQueue
{
    ValueTask EnqueueAsync(PushPayload payload);
    IAsyncEnumerable<PushPayload> ReadAllAsync(CancellationToken cancellationToken);
}

public class PushQueue : IPushQueue
{
    private readonly Channel<PushPayload> _channel;

    public PushQueue(IOptions<PushOptions> options)
    {
        // Bounded: if notifications back up, drop the oldest instead of growing without limit
        _channel = Channel.CreateBounded<PushPayload>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });
    }

    public ValueTask EnqueueAsync(PushPayload payload) => _channel.Writer.WriteAsync(payload);

    public IAsyncEnumerable<PushPayload> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
