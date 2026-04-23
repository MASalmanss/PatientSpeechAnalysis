namespace PatientSpeechAnalysis.Configuration;

public class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "PatientSpeechAnalysis";
    public string Audience { get; set; } = "PatientSpeechAnalysis";
    public int AccessTokenExpiryMinutes { get; set; } = 60;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
