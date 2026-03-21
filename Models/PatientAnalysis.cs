namespace PatientSpeechAnalysis.Models;

public class PatientAnalysis
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string PatientSentence { get; set; } = string.Empty;
    public string Mood { get; set; } = string.Empty;
    public bool IsEmergency { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int DailyScore { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
