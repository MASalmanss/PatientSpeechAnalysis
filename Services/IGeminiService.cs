using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public interface IGeminiService
{
    Task<GeminiAnalysisResult> AnalyzeAsync(string sentence);
    Task<SymptomExtractionResult> ExtractSymptomsAsync(string sentence);
}
