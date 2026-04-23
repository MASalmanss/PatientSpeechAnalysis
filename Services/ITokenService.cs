namespace PatientSpeechAnalysis.Services;

public interface ITokenService
{
    /// <summary>Yeni access + refresh token çifti üretir. Refresh token DB'ye hash'li kaydedilir.</summary>
    Task<TokenPair> GenerateTokenPairAsync();

    /// <summary>
    /// Raw refresh token doğrular; geçerliyse yeni access token + rotate edilmiş refresh token döner.
    /// Geçersizse null döner.
    /// </summary>
    Task<TokenPair?> RefreshAsync(string rawRefreshToken);

    /// <summary>Refresh token'ı iptal eder (logout).</summary>
    Task RevokeAsync(string rawRefreshToken);
}

public record TokenPair(string AccessToken, string RefreshToken);
