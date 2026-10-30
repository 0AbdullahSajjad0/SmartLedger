using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Receipts.Extraction;

namespace SmartLedger.Infrastructure.Extraction;

internal sealed class ReceiptExtractionWorker(
    ReceiptExtractionQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<GeminiOptions> options,
    ILogger<ReceiptExtractionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnqueuePendingAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            Guid receiptId;
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                wait.CancelAfter(SweepInterval);
                try
                {
                    receiptId = await queue.Reader.ReadAsync(wait.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    await EnqueuePendingAsync(stoppingToken);
                    continue;
                }
            }

            await ProcessAsync(receiptId, stoppingToken);
            await Task.Delay(options.Value.DelayBetweenRequests, stoppingToken);
        }
    }

    private async Task ProcessAsync(Guid receiptId, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IReceiptExtractionService>().ProcessAsync(receiptId, stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Reading receipt {ReceiptId} failed; it will be retried by the next sweep", receiptId);
        }
    }

    private async Task EnqueuePendingAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var pending = await scope.ServiceProvider.GetRequiredService<IReceiptExtractionService>().ListPendingAsync(stoppingToken);
            foreach (var receiptId in pending)
            {
                queue.Enqueue(receiptId);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Couldn't look for receipts waiting to be read; will try again later");
        }
    }
}
