using Microsoft.Extensions.Logging;
using SmartLedger.Application.Budgets;
using SmartLedger.Application.Categories;
using SmartLedger.Application.Common;
using SmartLedger.Application.Common.Storage;
using SmartLedger.Application.ExchangeRates;
using SmartLedger.Application.Receipts.Extraction;
using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.Application.Receipts;

internal sealed class ReceiptService(
    IReceiptRepository repository,
    IFileStorage storage,
    IReceiptExtractionQueue extractionQueue,
    ICategoryRepository categories,
    IUserDirectory users,
    IExchangeRateService exchangeRates,
    IBudgetAlertService budgetAlerts,
    TimeProvider timeProvider,
    ICurrentUser currentUser,
    ILogger<ReceiptService> logger) : IReceiptService
{
    private const int MaxPageSize = 50;

    private static readonly Error NotFound = Error.NotFound("receipts.not_found", "Receipt not found.");

    private static Error NotWaitingForReview(ReceiptStatus status) => status switch
    {
        ReceiptStatus.Processing => Error.Conflict("receipts.processing", "AI is still reading this receipt. Try again in a few seconds."),
        ReceiptStatus.Approved => Error.Conflict("receipts.approved", "This receipt is approved. Reopen it to make changes."),
        ReceiptStatus.Rejected => Error.Conflict("receipts.rejected", "This receipt is rejected. Reopen it to make changes."),
        _ => Error.Conflict("receipts.not_ready", "Fill in this receipt's details first."),
    };

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public async Task<Result<IReadOnlyList<ReceiptSummaryDto>>> UploadAsync(
        IReadOnlyList<UploadedFile> files, CancellationToken cancellationToken)
    {
        var userId = UserId;

        if (files.Count == 0)
        {
            return Error.Validation("receipts.no_files", "Choose at least one file.");
        }

        if (files.Count > ReceiptFileTypes.MaxFilesPerUpload)
        {
            return Error.Validation("receipts.too_many_files", $"Upload at most {ReceiptFileTypes.MaxFilesPerUpload} files at a time.");
        }

        var accepted = new List<(UploadedFile File, ReceiptFileType Type)>();
        foreach (var file in files)
        {
            var check = await IdentifyAsync(file, cancellationToken);
            if (!check.IsSuccess)
            {
                return check.Error!;
            }
            accepted.Add((file, check.Value));
        }

        var receipts = new List<Receipt>();
        try
        {
            foreach (var (file, type) in accepted)
            {
                var receiptId = Guid.CreateVersion7();
                var receipt = new Receipt
                {
                    Id = receiptId,
                    UserId = userId,
                    FileName = Path.GetFileName(file.FileName),
                    ContentType = type.ContentType,
                    SizeBytes = file.Length,
                    StorageKey = $"{userId}/{receiptId}{type.Extension}",
                };

                await using var content = file.OpenReadStream();
                await storage.SaveAsync(receipt.StorageKey, content, type.ContentType, cancellationToken);
                receipts.Add(receipt);
            }

            await repository.AddAsync(userId, receipts, cancellationToken);
        }
        catch
        {
            foreach (var receipt in receipts)
            {
                await storage.DeleteAsync(receipt.StorageKey, CancellationToken.None);
            }
            throw;
        }

        foreach (var receipt in receipts)
        {
            extractionQueue.Enqueue(receipt.Id);
        }

        return receipts.Select(ToSummary).ToList();
    }

    public async Task<PagedResult<ReceiptSummaryDto>> ListAsync(ReceiptQuery query, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        var (items, totalCount) = await repository.ListAsync(UserId, query.Status, Tidy(query.Filter), page, pageSize, cancellationToken);

        return new PagedResult<ReceiptSummaryDto>(items.Select(ToSummary).ToList(), page, pageSize, totalCount);
    }

    public async Task<Result<bool>> DeleteAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetForUpdateAsync(UserId, receiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound;
        }
        if (receipt.Status == ReceiptStatus.Processing)
        {
            return NotWaitingForReview(receipt.Status);
        }

        string[] files = [receipt.StorageKey, .. receipt.PreviewStorageKey is { } preview ? [preview] : Array.Empty<string>()];

        repository.Remove(receipt);
        await repository.SaveChangesAsync(cancellationToken);

        foreach (var key in files)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "File {StorageKey} of deleted receipt {ReceiptId} couldn't be removed", key, receiptId);
            }
        }

        return true;
    }

    private static ReceiptFilter Tidy(ReceiptFilter? filter)
    {
        if (filter is null)
        {
            return new ReceiptFilter();
        }

        var search = Clean(filter.Search);
        if (search is { Length: > 100 })
        {
            search = search[..100];
        }

        var (from, to) = filter is { From: { } f, To: { } t } && f > t ? (t, f) : (filter.From, filter.To);
        return filter with { Search = search, From = from, To = to };
    }

    public async Task<Result<ReceiptDetailDto>> GetAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetAsync(UserId, receiptId, cancellationToken);
        return receipt is null ? NotFound : ToDetail(receipt);
    }

    public async Task<Result<ReceiptFile>> OpenFileAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetAsync(UserId, receiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound;
        }

        var file = await storage.OpenReadAsync(receipt.StorageKey, cancellationToken);
        if (file is null)
        {
            return NotFound;
        }

        return new ReceiptFile(file.Content, receipt.ContentType,
            $"receipt-{receipt.Number:0000}{Path.GetExtension(receipt.StorageKey)}");
    }

    public async Task<Result<ReceiptFile>> OpenPreviewAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetAsync(UserId, receiptId, cancellationToken);
        if (receipt?.PreviewStorageKey is null)
        {
            return NotFound;
        }

        var file = await storage.OpenReadAsync(receipt.PreviewStorageKey, cancellationToken);
        if (file is null)
        {
            return NotFound;
        }

        return new ReceiptFile(file.Content, ReceiptFileTypes.Jpeg.ContentType,
            $"receipt-{receipt.Number:0000}-preview{ReceiptFileTypes.Jpeg.Extension}");
    }

    public async Task<Result<ReceiptDetailDto>> UpdateAsync(
        Guid receiptId, UpdateReceiptRequest request, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetForUpdateAsync(UserId, receiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound;
        }
        if (!receipt.IsEditable)
        {
            return NotWaitingForReview(receipt.Status);
        }

        var currency = Clean(request.Currency)?.ToUpperInvariant();
        if (currency is not null && !Currencies.IsKnown(currency))
        {
            return Error.Validation("receipts.unknown_currency", $"\"{currency}\" isn't a currency code we know.");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (request.ReceiptDate is { } date && (date > today.AddDays(1) || date < ExtractionSanitizer.EarliestPlausibleDate))
        {
            return Error.Validation("receipts.implausible_date", "The receipt date must be between 2000 and today.");
        }

        if (request.CategoryId is { } categoryId && categoryId != receipt.CategoryId)
        {
            var userCategories = await categories.ListAsync(UserId, cancellationToken);
            if (!userCategories.Any(c => c.Id == categoryId && !c.IsArchived))
            {
                return Error.Validation("receipts.unknown_category", "Choose one of your categories.");
            }
        }

        var lineItems = request.LineItems
            .Select((line, index) => new LineItem
            {
                ReceiptId = receipt.Id,
                LineNumber = index + 1,
                Description = line.Description.Trim(),
                Quantity = Math.Round(line.Quantity, 3, MidpointRounding.AwayFromZero),
                UnitPrice = Money(line.UnitPrice),
                Amount = Money(line.Amount),
            })
            .ToList();
        if (lineItems.Any(line => line.Description.Length == 0))
        {
            return Error.Validation("receipts.empty_line", "Every line item needs a description.");
        }

        var changed = new HashSet<string>(StringComparer.Ordinal);
        void Apply<T>(string field, T current, T updated, Action<T> assign)
        {
            if (!EqualityComparer<T>.Default.Equals(current, updated))
            {
                changed.Add(field);
                assign(updated);
            }
        }

        Apply("VendorName", receipt.VendorName, Clean(request.VendorName), value => receipt.VendorName = value);
        Apply("ReceiptDate", receipt.ReceiptDate, request.ReceiptDate, value => receipt.ReceiptDate = value);
        Apply("Currency", receipt.Currency, currency, value => receipt.Currency = value);
        Apply("Subtotal", receipt.Subtotal, Money(request.Subtotal), value => receipt.Subtotal = value);
        Apply("Tax", receipt.Tax, Money(request.Tax), value => receipt.Tax = value);
        Apply("Total", receipt.Total, Money(request.Total), value => receipt.Total = value);
        Apply("PaymentMethod", receipt.PaymentMethod, ExtractionSanitizer.MaskCardNumbers(Clean(request.PaymentMethod)),
            value => receipt.PaymentMethod = value);
        Apply("Category", receipt.CategoryId, request.CategoryId, value =>
        {
            receipt.CategoryId = value;
            receipt.CategorySuggestedByAi = false;
        });
        receipt.Notes = Clean(request.Notes);

        if (!SameLines(receipt.LineItems, lineItems))
        {
            changed.Add("LineItems");
            receipt.LineItems.Clear();
            receipt.LineItems.AddRange(lineItems);
        }

        receipt.ExtractionFlags = receipt.ExtractionFlags.Where(flag => !changed.Contains(flag.Field)).ToList();
        receipt.MarkEdited();

        await repository.SaveChangesAsync(cancellationToken);
        return ToDetail(receipt);
    }

    public async Task<Result<ReceiptDetailDto>> ApproveAsync(
        Guid receiptId, ApproveReceiptRequest request, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetForUpdateAsync(UserId, receiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound;
        }
        if (receipt.Status != ReceiptStatus.NeedsReview)
        {
            return NotWaitingForReview(receipt.Status);
        }
        if (!receipt.IsReadyToApprove)
        {
            return Error.Validation("receipts.incomplete", $"Add the {MissingForApproval(receipt)} before approving.");
        }

        var baseCurrency = await users.GetBaseCurrencyAsync(UserId, cancellationToken) ?? "USD";
        var total = receipt.Total!.Value;
        var receiptDate = receipt.ReceiptDate!.Value;

        decimal rate;
        DateOnly rateDate;
        decimal? amountCharged = null;
        if (request.AmountCharged is { } charged)
        {
            amountCharged = Money(charged);
            rate = total > 0
                ? Math.Round(amountCharged.Value / total, ExchangeRateService.RateDecimals, MidpointRounding.AwayFromZero)
                : 1m;
            rateDate = receiptDate;
        }
        else
        {
            var quote = await exchangeRates.GetRateAsync(receipt.Currency!, baseCurrency, receiptDate, cancellationToken);
            if (!quote.IsSuccess)
            {
                return quote.Error!;
            }
            rate = quote.Value.Rate;
            rateDate = quote.Value.RateDate;
        }

        receipt.Approve(baseCurrency, rate, rateDate, amountCharged, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);

        await budgetAlerts.CheckAsync(receipt, cancellationToken);
        return ToDetail(receipt);
    }

    public async Task<Result<ReceiptDetailDto>> RejectAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetForUpdateAsync(UserId, receiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound;
        }
        if (!receipt.IsEditable)
        {
            return NotWaitingForReview(receipt.Status);
        }

        receipt.Reject();
        await repository.SaveChangesAsync(cancellationToken);
        return ToDetail(receipt);
    }

    public async Task<Result<ReceiptDetailDto>> ReopenAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await repository.GetForUpdateAsync(UserId, receiptId, cancellationToken);
        if (receipt is null)
        {
            return NotFound;
        }
        if (receipt.Status is not (ReceiptStatus.Approved or ReceiptStatus.Rejected))
        {
            return Error.Conflict("receipts.not_decided", "Only approved or rejected receipts can be reopened.");
        }

        receipt.Reopen();
        await repository.SaveChangesAsync(cancellationToken);
        return ToDetail(receipt);
    }

    private static string MissingForApproval(Receipt receipt)
    {
        var missing = new List<string>();
        if (receipt.VendorName is null) missing.Add("vendor");
        if (receipt.ReceiptDate is null) missing.Add("date");
        if (receipt.Currency is null) missing.Add("currency");
        if (receipt.Total is null) missing.Add("total");
        if (receipt.CategoryId is null) missing.Add("category");

        return missing.Count == 1 ? missing[0] : $"{string.Join(", ", missing[..^1])} and {missing[^1]}";
    }

    private static bool SameLines(IReadOnlyList<LineItem> current, IReadOnlyList<LineItem> updated) =>
        current.Count == updated.Count
        && current.OrderBy(l => l.LineNumber).Zip(updated).All(pair =>
            pair.First.Description == pair.Second.Description
            && pair.First.Quantity == pair.Second.Quantity
            && pair.First.UnitPrice == pair.Second.UnitPrice
            && pair.First.Amount == pair.Second.Amount);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static decimal? Money(decimal? value) => value is null ? null : Money(value.Value);

    private static async Task<Result<ReceiptFileType>> IdentifyAsync(UploadedFile file, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(file.FileName);

        if (file.Length == 0)
        {
            return Error.Validation("receipts.empty_file", $"\"{name}\" is empty.");
        }

        if (file.Length > ReceiptFileTypes.MaxSizeBytes)
        {
            return Error.Validation("receipts.file_too_large", $"\"{name}\" is larger than 10 MB.");
        }

        var header = new byte[ReceiptFileTypes.SignatureLength];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

        return ReceiptFileTypes.Detect(header.AsSpan(0, read)) is { } type
            ? type
            : Error.Validation("receipts.unsupported_type", $"\"{name}\" isn't a JPG, PNG, WebP, HEIC or PDF file.");
    }

    internal static ReceiptSummaryDto ToSummary(Receipt r) => new(
        r.Id, r.Number, r.Status, r.VendorName, r.ReceiptDate, r.Currency, r.Total, r.BaseCurrencyTotal,
        r.CategoryId, r.FileName, r.CreatedAt);

    private static ReceiptDetailDto ToDetail(Receipt r) => new(
        r.Id, r.Number, r.Status, r.VendorName, r.ReceiptDate, r.Currency, r.Subtotal, r.Tax, r.Total,
        r.PaymentMethod, r.CategoryId, r.CategorySuggestedByAi, r.Notes, r.FailureReason,
        r.FileName, r.ContentType, r.SizeBytes, r.PreviewStorageKey is not null,
        r.LineItems.OrderBy(l => l.LineNumber)
            .Select(l => new LineItemDto(l.LineNumber, l.Description, l.Quantity, l.UnitPrice, l.Amount))
            .ToList(),
        r.ExtractionFlags.Select(f => new ExtractionFlagDto(f.Field, f.Reason)).ToList(),
        r.BaseCurrency, r.ExchangeRate, r.ExchangeRateDate, r.AmountCharged, r.BaseCurrencyTotal, r.ApprovedAt, r.CreatedAt);
}
