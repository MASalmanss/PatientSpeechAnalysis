using PatientSpeechAnalysis.Services;

namespace PatientSpeechAnalysis.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "psa_refresh";

    public static void MapAuthEndpoints(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AuthEndpoints");

        // POST /api/auth/token — İlk token çifti üret (login yok, client direkt ister)
        app.MapPost("/api/auth/token", async (ITokenService tokenService, HttpContext ctx) =>
        {
            try
            {
                var pair = await tokenService.GenerateTokenPairAsync();
                SetRefreshCookie(ctx, pair.RefreshToken, app.Environment.IsDevelopment());
                logger.LogInformation("Yeni token çifti verildi.");
                return Results.Ok(new { accessToken = pair.AccessToken });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Token üretilemedi.");
                return Results.Json(new { error = "Token üretilemedi." }, statusCode: 500);
            }
        })
        .WithName("GetToken")
        .WithOpenApi()
        .AllowAnonymous();

        // POST /api/auth/refresh — HttpOnly cookie'deki refresh token ile yeni çift al
        app.MapPost("/api/auth/refresh", async (ITokenService tokenService, HttpContext ctx) =>
        {
            var rawRefresh = ctx.Request.Cookies[RefreshCookieName];
            if (string.IsNullOrEmpty(rawRefresh))
            {
                logger.LogWarning("Refresh isteği: cookie yok.");
                return Results.Unauthorized();
            }

            try
            {
                var pair = await tokenService.RefreshAsync(rawRefresh);
                if (pair is null)
                {
                    DeleteRefreshCookie(ctx);
                    return Results.Unauthorized();
                }

                SetRefreshCookie(ctx, pair.RefreshToken, app.Environment.IsDevelopment());
                logger.LogInformation("Token yenilendi.");
                return Results.Ok(new { accessToken = pair.AccessToken });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Token yenileme hatası.");
                return Results.Json(new { error = "Token yenilenemedi." }, statusCode: 500);
            }
        })
        .WithName("RefreshToken")
        .WithOpenApi()
        .AllowAnonymous();

        // POST /api/auth/logout — Refresh token iptal et, cookie sil
        app.MapPost("/api/auth/logout", async (ITokenService tokenService, HttpContext ctx) =>
        {
            var rawRefresh = ctx.Request.Cookies[RefreshCookieName];
            if (!string.IsNullOrEmpty(rawRefresh))
                await tokenService.RevokeAsync(rawRefresh);

            DeleteRefreshCookie(ctx);
            logger.LogInformation("Oturum kapatıldı.");
            return Results.Ok(new { message = "Oturum kapatıldı." });
        })
        .WithName("Logout")
        .WithOpenApi()
        .AllowAnonymous();
    }

    private static void SetRefreshCookie(HttpContext ctx, string rawToken, bool isDevelopment)
    {
        ctx.Response.Cookies.Append(RefreshCookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !isDevelopment,        // Dev'de HTTP çalışsın, prod'da HTTPS zorunlu
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(7),
            Path = "/api/auth",             // Sadece auth endpoint'lerine gönderilir
        });
    }

    private static void DeleteRefreshCookie(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(RefreshCookieName, new CookieOptions
        {
            Path = "/api/auth"
        });
    }
}
