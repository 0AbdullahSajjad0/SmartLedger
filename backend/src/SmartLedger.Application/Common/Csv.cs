using System.Globalization;
using System.Text;

namespace SmartLedger.Application.Common;

internal static class Csv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public static void Row(StringBuilder csv, params string?[] cells)
    {
        csv.Append(string.Join(',', cells.Select(Cell))).Append("\r\n");
    }

    public static string? Number(decimal? value, string format = "0.00") =>
        value?.ToString(format, CultureInfo.InvariantCulture);

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

    private static bool IsNumber(string value) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
}
