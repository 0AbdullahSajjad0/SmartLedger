using System.Globalization;
using System.Text;

namespace SmartLedger.Application.Common;

/// <summary>
/// RFC 4180 CSV, safe to open in Excel. Text that starts like a formula (=, +, -, @) gets an apostrophe in front, so a
/// vendor named <c>=HYPERLINK(…)</c> on a receipt can't run as a formula on the accountant's computer.
/// </summary>
internal static class Csv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static void Row(StringBuilder csv, params string?[] cells)
    {
        csv.Append(string.Join(',', cells.Select(Cell))).Append("\r\n");
    }

    public static string? Number(decimal? value, string format = "0.00") =>
        value?.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>
    /// UTF-8 with a byte-order mark, so Excel shows "Café" and "····4242" correctly instead of guessing the encoding.
    /// </summary>
    public static byte[] ToBytes(StringBuilder csv) =>
        Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();

    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = FormulaStarts.Contains(value[0]) && !IsNumber(value) ? "'" + value : value;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    // Our own numbers (e.g. "-5.00" for a refund) are written as numbers; only text is guarded.
    private static bool IsNumber(string value) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
}
