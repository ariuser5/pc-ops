namespace AvatarController.Middleware;

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        // Keep middleware simple: only log brief request metadata at Debug level.
        if (_logger.IsEnabled(LogLevel.Information))
        {
            var req = context.Request;
            _logger.LogInformation("HTTP request: Method={Method}, Path={Path}, TraceId={TraceId}, Timestamp={Timestamp}",
                req.Method,
                req.Path,
                context.TraceIdentifier,
                DateTimeOffset.UtcNow);
        }

        await _next(context).ConfigureAwait(false);
    }
}
