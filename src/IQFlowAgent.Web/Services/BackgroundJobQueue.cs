using System.Threading.Channels;

namespace IQFlowAgent.Web.Services;

/// <summary>
/// Thread-safe queue for background RAG processing jobs.
/// Enqueue an intake ID; the <see cref="RagProcessorService"/> dequeues and processes it.
/// </summary>
public interface IBackgroundJobQueue
{
    void EnqueueRagJob(int ragJobId);
    ValueTask<int> DequeueAsync(CancellationToken cancellationToken);
}

public class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<int> _channel;

    public BackgroundJobQueue()
    {
        // Unbounded channel — we don't expect massive volume
        _channel = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    public void EnqueueRagJob(int ragJobId) =>
        _channel.Writer.TryWrite(ragJobId);

    public ValueTask<int> DequeueAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAsync(cancellationToken);
}
