using System.Text;
using System.Text.Encodings.Web;
using SmartLedger.Application.Email;

namespace SmartLedger.Infrastructure.Email;

/// <summary>
/// The look of every email: the app's ledger style, built from tables and inline styles because mail apps ignore
/// stylesheets and web fonts. Every value is HTML-encoded, since some (category names) are typed by users.
/// </summary>
internal static class EmailTemplate
{
    private const string Ink = "#1c1a17";
    private const string Paper = "#f4efe4";
    private const string Sheet = "#fbf8f1";
    private const string Muted = "#5e574c";
    private const string Accent = "#a84b26";

    /// <param name="eyebrow">The small capitals above the title, e.g. "Budget alert".</param>
    /// <param name="paragraphs">Plain sentences; encoded here.</param>
    /// <param name="footer">Small print under the sheet: why the email was sent.</param>
    public static EmailMessage Render(
        string to,
        string subject,
        string eyebrow,
        string title,
        IReadOnlyList<string> paragraphs,
        (string Label, Uri Url) button,
        string footer)
    {
        var html = new StringBuilder()
            .Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>").Append(Encode(subject)).Append("</title></head>")
            .Append($"<body style=\"margin:0;padding:0;background:{Paper};\">")
            .Append($"<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"background:{Paper};\"><tr><td align=\"center\" style=\"padding:32px 16px;\">")
            .Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:560px;\">")
            // Masthead: the wordmark on espresso.
            .Append($"<tr><td style=\"background:{Ink};padding:16px 28px;font-family:Georgia,'Times New Roman',serif;font-size:20px;font-weight:bold;color:{Paper};\">SmartLedger</td></tr>")
            // The sheet.
            .Append($"<tr><td style=\"background:{Sheet};border:1px solid {Ink};border-top:0;padding:32px 28px;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.6;color:{Ink};\">")
            .Append($"<p style=\"margin:0 0 8px;font-size:11px;letter-spacing:2px;text-transform:uppercase;color:{Accent};\">")
            .Append(Encode(eyebrow)).Append("</p>")
            .Append($"<h1 style=\"margin:0 0 20px;padding-bottom:16px;border-bottom:2px solid {Ink};font-family:Georgia,'Times New Roman',serif;font-size:26px;line-height:1.25;font-weight:bold;\">")
            .Append(Encode(title)).Append("</h1>");

        foreach (var paragraph in paragraphs)
        {
            html.Append("<p style=\"margin:0 0 16px;\">").Append(Encode(paragraph)).Append("</p>");
        }

        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"margin:8px 0 4px;\"><tr>")
            .Append($"<td style=\"background:{Accent};\"><a href=\"{Encode(button.Url.AbsoluteUri)}\" style=\"display:inline-block;padding:12px 22px;font-family:Arial,Helvetica,sans-serif;font-size:13px;font-weight:bold;letter-spacing:1px;text-transform:uppercase;color:#ffffff;text-decoration:none;\">")
            .Append(Encode(button.Label)).Append("</a></td></tr></table>")
            .Append($"<p style=\"margin:16px 0 0;font-size:12px;color:{Muted};word-break:break-all;\">Or open this address: ")
            .Append(Encode(button.Url.AbsoluteUri)).Append("</p>")
            .Append("</td></tr>")
            .Append($"<tr><td style=\"padding:16px 4px;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.5;color:{Muted};\">")
            .Append(Encode(footer)).Append("</td></tr>")
            .Append("</table></td></tr></table></body></html>");

        var text = new StringBuilder()
            .Append(title).Append("\r\n\r\n");
        foreach (var paragraph in paragraphs)
        {
            text.Append(paragraph).Append("\r\n\r\n");
        }
        text.Append(button.Label).Append(": ").Append(button.Url.AbsoluteUri).Append("\r\n\r\n")
            .Append("-- \r\n").Append(footer).Append("\r\n");

        return new EmailMessage(to, subject, text.ToString(), html.ToString());
    }

    private static string Encode(string value) => HtmlEncoder.Default.Encode(value);

}
