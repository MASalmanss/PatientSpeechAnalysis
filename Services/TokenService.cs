using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PatientSpeechAnalysis.Configuration;
using PatientSpeechAnalysis.Data;
using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public class TokenService : ITokenService
{
    private readonly JwtOptions _jwt;
    private readonly AppDbContext _db;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IOptions<JwtOptions> jwt, AppDbContext db, ILogger<TokenService> logger)
    {
        _jwt = jwt.Value;
        _db = db;
        _logger = logger;
    }

    public async Task<TokenPair> GenerateTokenPairAsync()
    {
        var access = BuildAccessToken();
        var (raw, entity) = BuildRefreshToken();

        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Token çifti oluşturuldu (refresh exp: {Exp:u})", entity.ExpiresAt);
        return new TokenPair(access, raw);
    }

    public async Task<TokenPair?> RefreshAsync(string rawRefreshToken)
    {
        var hash = Hash(rawRefreshToken);
        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow);

        if (existing is null)
        {
            _logger.LogWarning("Refresh token geçersiz veya süresi dolmuş.");
            return null;
        }

        // Token rotation: eskiyi iptal et, yeni çift üret
        existing.IsRevoked = true;
        var (newRaw, newEntity) = BuildRefreshToken();
        _db.RefreshTokens.Add(newEntity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Token rotate edildi.");
        return new TokenPair(BuildAccessToken(), newRaw);
    }

    public async Task RevokeAsync(string rawRefreshToken)
    {
        var hash = Hash(rawRefreshToken);
        var token = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == hash && !t.IsRevoked);

        if (token is not null)
        {
            token.IsRevoked = true;
            await _db.SaveChangesAsync();
            _logger.LogInformation("Refresh token iptal edildi.");
        }
    }

    // ── Yardımcı metodlar ────────────────────────────────────────────────────

    private string BuildAccessToken()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "psa-client"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
        };

        var token = new JwtSecurityToken(
            issuer: _jwt.Issuer,
            audience: _jwt.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_jwt.AccessTokenExpiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private (string Raw, RefreshToken Entity) BuildRefreshToken()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var entity = new RefreshToken
        {
            TokenHash = Hash(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(_jwt.RefreshTokenExpiryDays),
            CreatedAt = DateTime.UtcNow,
        };
        return (raw, entity);
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
