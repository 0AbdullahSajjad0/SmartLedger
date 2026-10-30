using System.Threading.Channels;
using SmartLedger.Application.Receipts.Extraction;

namespace SmartLedger.Infrastructure.Extraction;

internal sealed class ReceiptExtractionQueue : IReceiptExtractionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ChannelReader<Guid> Reader => _channel.Reader;

    public void Enqueue(Guid receiptId) => _channel.Writer.TryWrite(receiptId);
}
