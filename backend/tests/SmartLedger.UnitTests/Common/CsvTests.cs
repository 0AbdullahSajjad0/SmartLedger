using System.Text;
using SmartLedger.Application.Common;

namespace SmartLedger.UnitTests.Common;

public class CsvTests
{
    [Theory]
    [InlineData("=SUM(A1:A9)", "'=SUM(A1:A9)")]
    [InlineData("+44 20 7946", "'+44 20 7946")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@cmd", "'@cmd")]
    public void TextThatLooksLikeAFormula_IsDefused(string value, string written)
    {
        Assert.Equal(written, Write(value));
    }

    [Theory]
    [InlineData("-5.00")]
    [InlineData("126.45")]
    public void OurOwnNumbers_StayNumbers(string value)
    {
        Assert.Equal(value, Write(value));
    }

    [Theory]
    [InlineData("Team, offsite", "\"Team, offsite\"")]
    [InlineData("The \"best\" café", "\"The \"\"best\"\" café\"")]
    [InlineData("Line one\nline two", "\"Line one\nline two\"")]
    public void CommasQuotesAndLineBreaks_AreQuoted(string value, string written)
    {
        Assert.Equal(written, Write(value));
    }

    [Fact]
    public void EmptyCells_StayEmpty_AndRowsEndWithCrLf()
    {
        var csv = new StringBuilder();

        Csv.Row(csv, "a", null, "", "b");

        Assert.Equal("a,,,b\r\n", csv.ToString());
    }

    private static string Write(string value)
    {
        var csv = new StringBuilder();
        Csv.Row(csv, value);
        return csv.ToString()[..^2];
    }
}
