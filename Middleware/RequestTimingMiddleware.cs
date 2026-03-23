using System.Diagnostics;

namespace PatientSpeechAnalysis.Middleware;

public class RequestTimingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestTimingMiddleware> _logger;

    public RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();
            _logger.LogInformation(
                "REQUEST {Method} {Path} -> {StatusCode} in {Elapsed:F3}s",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                sw.Elapsed.TotalSeconds);
        }
    }
}
