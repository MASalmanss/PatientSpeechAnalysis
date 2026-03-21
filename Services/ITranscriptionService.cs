namespace PatientSpeechAnalysis.Services;

public interface ITranscriptionService
{
    Task<string> TranscribeAsync(byte[] audioBytes, string fileName);
}
