using System.Diagnostics;
using PatientSpeechAnalysis.Data;
using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public class AnalysisService : IAnalysisService
{
    private readonly IGeminiService _geminiService;
    private readonly IEmailService _emailService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AnalysisService> _logger;

    public AnalysisService(
        IGeminiService geminiService,
        IEmailService emailService,
        AppDbContext dbContext,
        ILogger<AnalysisService> logger)
    {
        _geminiService = geminiService;
        _emailService = emailService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PatientAnalysis> AnalyzeAsync(int patientId, string sentence)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Hasta #{PatientId} için analiz başlatılıyor...", patientId);

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
        _logger.LogInformation("Analiz DB'ye kaydedildi - ID: {AnalysisId}", analysis.Id);

        if (analysis.IsEmergency)
        {
            _logger.LogWarning("ACİL DURUM TESPİT EDİLDİ - Hasta #{PatientId}! E-posta gönderiliyor...", patientId);
            await _emailService.SendEmergencyEmailAsync(analysis);
        }

        sw.Stop();
        _logger.LogInformation("Hasta #{PatientId} analizi tamamlandı ({Elapsed:F3}s)", patientId, sw.Elapsed.TotalSeconds);
        return analysis;
    }
}
