using PatientSpeechAnalysis.Data;
using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public class AnalysisService : IAnalysisService
{
    private readonly IGeminiService _geminiService;
    private readonly IEmailService _emailService;
    private readonly AppDbContext _dbContext;

    public AnalysisService(IGeminiService geminiService, IEmailService emailService, AppDbContext dbContext)
    {
        _geminiService = geminiService;
        _emailService = emailService;
        _dbContext = dbContext;
    }

    public async Task<PatientAnalysis> AnalyzeAsync(int patientId, string sentence)
    {
        Console.WriteLine($"[AnalysisService] Hasta #{patientId} için analiz başlatılıyor...");

        var geminiResult = await _geminiService.AnalyzeAsync(sentence);

        var analysis = new PatientAnalysis
        {
            PatientId = patientId,
            PatientSentence = sentence,
            Mood = geminiResult.Mood,
            IsEmergency = geminiResult.IsEmergency,
            Summary = geminiResult.Summary,
            DailyScore = geminiResult.DailyScore,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PatientAnalyses.Add(analysis);
        await _dbContext.SaveChangesAsync();
        Console.WriteLine($"[AnalysisService] Analiz DB'ye kaydedildi - ID: {analysis.Id}");

        if (analysis.IsEmergency)
        {
            Console.WriteLine($"[AnalysisService] ACİL DURUM TESPİT EDİLDİ - Hasta #{patientId}! E-posta gönderiliyor...");
            await _emailService.SendEmergencyEmailAsync(analysis);
        }

        Console.WriteLine($"[AnalysisService] Hasta #{patientId} analizi tamamlandı.");
        return analysis;
    }
}
