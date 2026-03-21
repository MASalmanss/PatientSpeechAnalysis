using System.Text.Json.Serialization;

namespace PatientSpeechAnalysis.Models;

public class GeminiAnalysisResult
{
    [JsonPropertyName("mood")]
    public string Mood { get; set; } = string.Empty;

    [JsonPropertyName("isEmergency")]
    public bool IsEmergency { get; set; }

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    [JsonPropertyName("dailyScore")]
    public int DailyScore { get; set; }
}
