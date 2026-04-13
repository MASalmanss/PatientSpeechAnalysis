namespace PatientSpeechAnalysis.Services;

public interface ITtsService
{
    Task<byte[]> SynthesizeAsync(string text);
}
