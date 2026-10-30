using SmartLedger.Domain.Entities;
using SmartLedger.Domain.Enums;

namespace SmartLedger.UnitTests.Domain;

public class BudgetAlertTests
{
    [Theory]
    [InlineData("79.99", "100", null)]
    [InlineData("80", "100", BudgetAlertLevel.Warning)]
    [InlineData("99.99", "100", BudgetAlertLevel.Warning)]
    [InlineData("100", "100", BudgetAlertLevel.Warning)] // Used up, not over.
    [InlineData("100.01", "100", BudgetAlertLevel.Exceeded)]
    [InlineData("250", "100", BudgetAlertLevel.Exceeded)]
    [InlineData("10", "0", null)] // A zero budget means "no budget", not "always over".
    public void LevelFor_WarnsAt80Percent_AndExceedsAt100(string spent, string budget, BudgetAlertLevel? expected)
    {
        Assert.Equal(expected, BudgetAlert.LevelFor(decimal.Parse(spent), decimal.Parse(budget)));
    }
}
