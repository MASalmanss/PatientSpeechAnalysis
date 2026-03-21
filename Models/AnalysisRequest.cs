namespace PatientSpeechAnalysis.Models;

public class AnalysisRequest
{
    public int PatientId { get; set; }
    public string Sentence { get; set; } = string.Empty;
}
