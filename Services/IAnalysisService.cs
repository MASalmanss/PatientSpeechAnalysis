using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public interface IAnalysisService
{
    Task<PatientAnalysis> AnalyzeAsync(int patientId, string sentence);
}
