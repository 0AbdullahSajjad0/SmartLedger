using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SmartLedger.Application.Common;
using SmartLedger.Application.Common.Storage;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Application.Account;

/// <summary>
/// Builds the "export my data" archive: <c>account.json</c> with everything, <c>receipts.csv</c> and
/// <c>line-items.csv</c> for spreadsheets, and <c>files/</c> with every upload exactly as it was received.
/// Previews are left out; they are copies made from the originals.
/// </summary>
internal sealed class AccountExportService(
    IAccountDataReader reader,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider timeProvider,
    ILogger<AccountExportService> logger) : IAccountExportService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private Guid UserId => currentUser.UserId ?? throw new InvalidOperationException("No signed-in user.");

    public async Task<Result<AccountExport>> PrepareAsync(CancellationToken cancellationToken)
    {
        var data = await reader.ReadAsync(UserId, cancellationToken);
        if (data is null)
        {
            return Error.NotFound("account.not_found", "The account no longer exists.");
        }

        var exportedAt = timeProvider.GetUtcNow();
        return new AccountExport(
            $"smartledger-export-{exportedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip",
            (destination, token) => WriteAsync(data, exportedAt, destination, token));
    }

    private async Task WriteAsync(AccountData data, DateTimeOffset exportedAt, Stream destination, CancellationToken cancellationToken)
    {
        await using var output = new AsyncOnlyWriteStream(destination);
        await WriteArchiveAsync(data, exportedAt, output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private async Task WriteArchiveAsync(AccountData data, DateTimeOffset exportedAt, Stream destination, CancellationToken cancellationToken)
    {
        await using var zip = await ZipArchive.CreateAsync(destination, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);

        // The files first, so the data written after them can say which ones made it in.
        var files = new Dictionary<Guid, string>();
        foreach (var receipt in data.Receipts)
        {
            if (await AddOriginalAsync(zip, receipt, cancellationToken) is { } path)
            {
                files[receipt.Id] = path;
            }
        }

        var categoryNames = data.Categories.ToDictionary(c => c.Id, c => c.Name);
        var export = ToExport(data, exportedAt, categoryNames, files);

        await AddTextAsync(zip, "account.json", Encoding.UTF8.GetBytes(JsonSerializer.Serialize(export, Json)), cancellationToken);
        await AddTextAsync(zip, "receipts.csv", ReceiptsCsv(data.Receipts, categoryNames, files), cancellationToken);
        await AddTextAsync(zip, "line-items.csv", LineItemsCsv(data.Receipts), cancellationToken);
        await AddTextAsync(zip, "README.txt", Encoding.UTF8.GetBytes(Readme(data, exportedAt, files)), cancellationToken);
    }

    /// <summary>
    /// Copies the upload into <c>files/</c>, named by entry number. Null when it isn't in storage. A failure while
    /// copying is not caught: half a file can't be taken back out of the archive, so the download fails instead.
    /// </summary>
    private async Task<string?> AddOriginalAsync(ZipArchive zip, Receipt receipt, CancellationToken cancellationToken)
    {
        var stored = await storage.OpenReadAsync(receipt.StorageKey, cancellationToken);
        if (stored is null)
        {
            logger.LogWarning("Receipt {ReceiptId}'s file {StorageKey} is missing from storage; left out of the export",
                receipt.Id, receipt.StorageKey);
            return null;
        }

        await using (stored.Content)
        {
            // The extension of the type detected at upload (the storage key ends with it), never the user's file name.
            var path = $"files/receipt-{receipt.Number.ToString("0000", CultureInfo.InvariantCulture)}{Path.GetExtension(receipt.StorageKey)}";

            // Photos and PDFs are already compressed; squeezing them again costs CPU for almost nothing.
            var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);
            entry.LastWriteTime = receipt.CreatedAt;

            await using var target = await entry.OpenAsync(cancellationToken);
            await stored.Content.CopyToAsync(target, cancellationToken);
            return path;
        }
    }

    private static async Task AddTextAsync(ZipArchive zip, string path, byte[] content, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var target = await entry.OpenAsync(cancellationToken);
        await target.WriteAsync(content, cancellationToken);
    }

    private static ExportDocument ToExport(
        AccountData data,
        DateTimeOffset exportedAt,
        IReadOnlyDictionary<Guid, string> categoryNames,
        IReadOnlyDictionary<Guid, string> files) => new(
        exportedAt,
        new ExportAccount(data.Profile.Email, data.Profile.DisplayName, data.Profile.BaseCurrency),
        data.Categories.Select(c => new ExportCategory(
            c.Id,
            c.Name,
            c.Color,
            c.IsArchived,
            data.Budgets
                .Where(b => b.CategoryId == c.Id)
                .OrderBy(b => b.EffectiveFrom)
                .Select(b => new ExportBudget(Months.Format(b.EffectiveFrom), b.Amount))
                .ToList())).ToList(),
        data.Receipts.Select(r => new ExportReceipt(
            r.Id,
            r.Number,
            r.Status.ToString(),
            r.VendorName,
            r.ReceiptDate,
            r.Currency,
            r.Subtotal,
            r.Tax,
            r.Total,
            r.PaymentMethod,
            r.CategoryId,
            r.CategoryId is { } id ? categoryNames.GetValueOrDefault(id) : null,
            r.Notes,
            r.LineItems
                .OrderBy(l => l.LineNumber)
                .Select(l => new ExportLineItem(l.LineNumber, l.Description, l.Quantity, l.UnitPrice, l.Amount))
                .ToList(),
            r.BaseCurrency,
            r.ExchangeRate,
            r.ExchangeRateDate,
            r.AmountCharged,
            r.BaseCurrencyTotal,
            r.ApprovedAt,
            r.FailureReason,
            r.ExtractionFlags.Select(f => new ExportFlag(f.Field, f.Reason)).ToList(),
            r.CreatedAt,
            r.FileName,
            files.GetValueOrDefault(r.Id))).ToList(),
        data.Insights
            .Select(i => new ExportAnalysis($"{i.Year:0000}-{i.Month:00}", i.Headline, i.Body, i.GeneratedAt))
            .ToList());

    private static byte[] ReceiptsCsv(
        IReadOnlyList<Receipt> receipts,
        IReadOnlyDictionary<Guid, string> categoryNames,
        IReadOnlyDictionary<Guid, string> files)
    {
        var csv = new StringBuilder();
        Csv.Row(csv, "Entry", "Status", "Date", "Vendor", "Category", "Currency", "Subtotal", "Tax", "Total",
            "Base currency", "Exchange rate", "Amount charged", "Base currency total", "Paid with", "Notes",
            "Uploaded", "File");
        foreach (var r in receipts)
        {
            Csv.Row(csv,
                r.Number.ToString("0000", CultureInfo.InvariantCulture),
                r.Status.ToString(),
                r.ReceiptDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.VendorName,
                r.CategoryId is { } id ? categoryNames.GetValueOrDefault(id) : null,
                r.Currency,
                Csv.Number(r.Subtotal),
                Csv.Number(r.Tax),
                Csv.Number(r.Total),
                r.BaseCurrency,
                Csv.Number(r.ExchangeRate, "0.##########"),
                Csv.Number(r.AmountCharged),
                Csv.Number(r.BaseCurrencyTotal),
                r.PaymentMethod,
                r.Notes,
                r.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                files.GetValueOrDefault(r.Id));
        }
        return Csv.ToBytes(csv);
    }

    private static byte[] LineItemsCsv(IReadOnlyList<Receipt> receipts)
    {
        var csv = new StringBuilder();
        Csv.Row(csv, "Entry", "Line", "Description", "Quantity", "Unit price", "Amount", "Currency");
        foreach (var r in receipts)
        {
            foreach (var line in r.LineItems.OrderBy(l => l.LineNumber))
            {
                Csv.Row(csv,
                    r.Number.ToString("0000", CultureInfo.InvariantCulture),
                    line.LineNumber.ToString(CultureInfo.InvariantCulture),
                    line.Description,
                    Csv.Number(line.Quantity, "0.###"),
                    Csv.Number(line.UnitPrice),
                    Csv.Number(line.Amount),
                    r.Currency);
            }
        }
        return Csv.ToBytes(csv);
    }

    private static string Readme(AccountData data, DateTimeOffset exportedAt, IReadOnlyDictionary<Guid, string> files)
    {
        var text = new StringBuilder()
            .Append("SmartLedger data export\r\n")
            .Append(CultureInfo.InvariantCulture, $"Exported {exportedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC for {data.Profile.Email}.\r\n\r\n")
            .Append("account.json     Your account, categories and budgets, every receipt with its line items,\r\n")
            .Append("                 and the monthly analyses AI wrote for you.\r\n")
            .Append("receipts.csv     One row per receipt, for a spreadsheet.\r\n")
            .Append("line-items.csv   One row per line on a receipt.\r\n")
            .Append("files/           Every receipt exactly as you uploaded it, named by entry number\r\n")
            .Append("                 (receipt-0001.jpg is entry 0001). Photos keep any location your camera saved in them.\r\n");

        var missing = data.Receipts.Where(r => !files.ContainsKey(r.Id)).Select(r => r.Number.ToString("0000", CultureInfo.InvariantCulture)).ToList();
        if (missing.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"\r\nThe file of entry {string.Join(", ", missing)} could not be found and is not included.\r\n");
        }

        return text.ToString();
    }

    // The archive's JSON, spelled out so the export stays the same when the database changes.
    private sealed record ExportDocument(
        DateTimeOffset ExportedAt,
        ExportAccount Account,
        IReadOnlyList<ExportCategory> Categories,
        IReadOnlyList<ExportReceipt> Receipts,
        IReadOnlyList<ExportAnalysis> MonthlyAnalyses);

    private sealed record ExportAccount(string Email, string DisplayName, string BaseCurrency);

    /// <param name="Budgets">Every budget change, oldest first: each applies from its month until the next one.</param>
    private sealed record ExportCategory(Guid Id, string Name, string Color, bool IsArchived, IReadOnlyList<ExportBudget> Budgets);

    /// <param name="From">"yyyy-MM".</param>
    /// <param name="MonthlyBudget">Null means no budget from that month.</param>
    private sealed record ExportBudget(string From, decimal? MonthlyBudget);

    private sealed record ExportReceipt(
        Guid Id,
        int Number,
        string Status,
        string? VendorName,
        DateOnly? ReceiptDate,
        string? Currency,
        decimal? Subtotal,
        decimal? Tax,
        decimal? Total,
        string? PaymentMethod,
        Guid? CategoryId,
        string? CategoryName,
        string? Notes,
        IReadOnlyList<ExportLineItem> LineItems,
        string? BaseCurrency,
        decimal? ExchangeRate,
        DateOnly? ExchangeRateDate,
        decimal? AmountCharged,
        decimal? BaseCurrencyTotal,
        DateTimeOffset? ApprovedAt,
        string? FailureReason,
        IReadOnlyList<ExportFlag> AiFlags,
        DateTimeOffset UploadedAt,
        string OriginalFileName,
        string? File);

    private sealed record ExportLineItem(int Line, string Description, decimal Quantity, decimal UnitPrice, decimal Amount);

    private sealed record ExportFlag(string Field, string Reason);

    private sealed record ExportAnalysis(string Month, string Headline, string Body, DateTimeOffset WrittenAt);

    /// <summary>
    /// .NET 10's ZIP writer still writes one small record per file synchronously (the 16–24 byte data descriptor,
    /// used when the output can't seek, like a download). ASP.NET Core refuses synchronous writes to a response rather
    /// than block a thread, so those bytes wait here and go out with the next asynchronous write or flush.
    /// </summary>
    private sealed class AsyncOnlyWriteStream(Stream inner) : Stream
    {
        private readonly MemoryStream _pending = new();

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => _pending.Write(buffer, offset, count);

        public override void Write(ReadOnlySpan<byte> buffer) => _pending.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await SendPendingAsync(cancellationToken);
            await inner.WriteAsync(buffer, cancellationToken);
        }

        // Nothing is sent synchronously; FlushAsync does the real work.
        public override void Flush()
        {
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            await SendPendingAsync(cancellationToken);
            await inner.FlushAsync(cancellationToken);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override async ValueTask DisposeAsync()
        {
            await SendPendingAsync(CancellationToken.None);
            await _pending.DisposeAsync();
            await base.DisposeAsync();
        }

        private async Task SendPendingAsync(CancellationToken cancellationToken)
        {
            if (_pending.Length == 0)
            {
                return;
            }

            await inner.WriteAsync(_pending.GetBuffer().AsMemory(0, (int)_pending.Length), cancellationToken);
            _pending.SetLength(0);
        }
    }
}
