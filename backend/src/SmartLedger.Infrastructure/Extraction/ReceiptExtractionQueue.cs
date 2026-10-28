using System.Threading.Channels;
using SmartLedger.Application.Receipts.Extraction;

namespace SmartLedger.Infrastructure.Extraction;

/// <summary>
/// An in-memory queue between upload requests and the background worker. Nothing is lost if the app
/// restarts: receipts still marked Processing in the database are queued again when the worker starts.
/// </summary>
internal sealed class ReceiptExtractionQueue : IReceiptExtractionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid receiptId) => _channel.Writer.TryWrite(receiptId);
}
