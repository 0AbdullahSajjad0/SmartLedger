using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartLedger.Application.Receipts.Extraction;

namespace SmartLedger.Infrastructure.Extraction;

/// <summary>
/// Reads uploaded receipts one at a time, in the background. One at a time with a pause between them keeps
/// within the AI free tier's rate limit; a periodic sweep retries receipts whose earlier attempt failed.
/// </summary>
internal sealed class ReceiptExtractionWorker(
    ReceiptExtractionQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<GeminiOptions> options,
    ILogger<ReceiptExtractionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Receipts uploaded before a restart, or left by a failed attempt.
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
                    // Quiet for a while: look for receipts that still need another attempt.
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
            // An unexpected failure (e.g. the database is unreachable) must not stop the worker for everyone.
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
