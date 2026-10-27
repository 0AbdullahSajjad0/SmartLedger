namespace SmartLedger.Api.Security;

internal static class SecurityHeaders
{
    /// <summary>
    /// Headers on every API response: never guess a response's type (so uploaded bytes can't be run as HTML),
    /// never show the API inside a frame, and never leak the URL to other sites.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            await next();
        });

    /// <summary>
    /// For user-uploaded files: even if a file is opened directly in the browser, it can't run scripts,
    /// load anything, or act as part of the site.
    /// </summary>
    public const string UploadedFileContentSecurityPolicy = "default-src 'none'; img-src 'self'; sandbox";
}
