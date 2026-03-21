using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public interface IEmailService
{
    Task SendEmergencyEmailAsync(PatientAnalysis analysis);
}
