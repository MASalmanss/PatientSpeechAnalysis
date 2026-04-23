namespace PatientSpeechAnalysis.Models;

public class RefreshToken
{
    public int Id { get; set; }

    /// <summary>SHA-256 of the raw token — raw değer asla DB'ye yazılmaz.</summary>
    public string TokenHash { get; set; } = "";

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRevoked { get; set; }
}
