using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartLedger.Domain.Entities;
using SmartLedger.Infrastructure.Identity;

namespace SmartLedger.Infrastructure.Persistence;

/// <summary>
/// Creates a demo user with the categories and receipts from the design prototype, so a fresh
/// local database has realistic data. Runs only in Development and does nothing if the user exists.
/// </summary>
public sealed class DevelopmentDataSeeder(AppDbContext db, UserManager<ApplicationUser> userManager, TimeProvider timeProvider)
{
    public const string DemoEmail = "demo@smartledger.local";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await userManager.FindByEmailAsync(DemoEmail) is not null)
        {
            return;
        }

        // No password: this account exists only to have data to look at locally.
        var user = new ApplicationUser
        {
            UserName = DemoEmail,
            Email = DemoEmail,
            EmailConfirmed = true,
            DisplayName = "Demo account",
            BaseCurrency = "USD",
        };

        var result = await userManager.CreateAsync(user);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Could not create the demo user: " + string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        var categories = CreateCategories(user.Id);
        db.Categories.AddRange(categories.Values.Select(c => c.Category));
        var receipts = CreateReceipts(user.Id, categories.ToDictionary(c => c.Key, c => c.Value.Category)).ToList();
        db.Receipts.AddRange(receipts);

        // The budgets apply from the month of the oldest demo receipt, so every month shown has them.
        var firstMonth = CategoryBudget.FirstOfMonth(receipts.Min(r => r.ReceiptDate!.Value));
        db.CategoryBudgets.AddRange(categories.Values.Select(c =>
            new CategoryBudget { CategoryId = c.Category.Id, EffectiveFrom = firstMonth, Amount = c.Budget }));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, (Category Category, decimal Budget)> CreateCategories(Guid userId)
    {
        (Category, decimal) Create(string name, string color, decimal budget) =>
            (new Category { UserId = userId, Name = name, Color = color }, budget);

        return new[]
        {
            Create("Travel", "#3D4A2F", 1500m),
            Create("Software & Subscriptions", "#5B6B73", 700m),
            Create("Lodging", "#7A5C45", 800m),
            Create("Food & Dining", "#B08A2E", 600m),
            Create("Office Supplies", "#8C8270", 500m),
            Create("Utilities", "#4A4038", 400m),
        }.ToDictionary(c => c.Item1.Name);
    }

    private IEnumerable<Receipt> CreateReceipts(Guid userId, Dictionary<string, Category> categories)
    {
        var now = timeProvider.GetUtcNow();

        Receipt Create(int number, string vendor, string date, string category, string payment, decimal total, string notes)
        {
            return new Receipt
            {
                UserId = userId,
                Number = number,
                VendorName = vendor,
                ReceiptDate = DateOnly.Parse(date, CultureInfo.InvariantCulture),
                Currency = "USD",
                Total = total,
                PaymentMethod = payment,
                CategoryId = categories[category].Id,
                CategorySuggestedByAi = true,
                Notes = notes,
                FileName = $"receipt-{number:0000}.jpg",
                ContentType = "image/jpeg",
                SizeBytes = 0,
                StorageKey = $"demo/receipt-{number:0000}.jpg",
            };
        }

        Receipt Approved(Receipt receipt)
        {
            receipt.MarkExtracted();
            receipt.Approve("USD", 1m, receipt.ReceiptDate!.Value, amountCharged: null, now);
            return receipt;
        }

        Receipt NeedsReview(Receipt receipt)
        {
            receipt.MarkExtracted();
            return receipt;
        }

        var paperCo = Create(425, "Paper & Co. Office Supply", "2026-10-19", "Office Supplies", "VISA ····4242", 126.45m, "3 line items");
        paperCo.Subtotal = 118.45m;
        paperCo.Tax = 8.00m;
        paperCo.LineItems =
        [
            new LineItem { LineNumber = 1, Description = "Printer paper, 5 reams", Quantity = 5, UnitPrice = 8.99m, Amount = 44.95m },
            new LineItem { LineNumber = 2, Description = "Toner cartridge", Quantity = 1, UnitPrice = 64.50m, Amount = 64.50m },
            new LineItem { LineNumber = 3, Description = "Sticky notes", Quantity = 4, UnitPrice = 2.25m, Amount = 9.00m },
        ];
        paperCo.ExtractionFlags = [new ExtractionFlag("PaymentMethod", "One digit of the card number was faded on the original.")];

        var duplicate = NeedsReview(Create(420, "Corner Deli", "2026-10-11", "Food & Dining", "CASH", 15.25m, "Duplicate of entry 0418"));
        duplicate.Reject();

        return
        [
            NeedsReview(Create(427, "Skyway Airlines", "2026-10-21", "Travel", "VISA ····4242", 482.00m, "Flight LHE to KHI")),
            NeedsReview(Create(426, "Northside Café", "2026-10-20", "Food & Dining", "CASH", 38.60m, "Client lunch")),
            NeedsReview(paperCo),
            Approved(Create(424, "CloudHost Inc.", "2026-10-18", "Software & Subscriptions", "MC ····8810", 89.00m, "Monthly hosting")),
            Approved(Create(423, "Cityline Taxi", "2026-10-17", "Travel", "CASH", 24.80m, "Airport transfer")),
            Approved(Create(422, "Grand Plaza Hotel", "2026-10-15", "Lodging", "VISA ····4242", 670.15m, "3 nights")),
            Approved(Create(421, "Volt Electric", "2026-10-12", "Utilities", "TRANSFER", 356.20m, "September bill")),
            duplicate,
        ];
    }
}
