using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PatientSpeechAnalysis.Data;
using PatientSpeechAnalysis.Messaging;
using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public class AnalysisService : IAnalysisService
{
    private readonly IGeminiService _geminiService;
    private readonly IEmailService _emailService;
    private readonly IRabbitMqRpcClient _rabbitMq;
    private readonly RabbitMqOptions _mqOptions;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AnalysisService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AnalysisService(
        IGeminiService geminiService,
        IEmailService emailService,
        IRabbitMqRpcClient rabbitMq,
        IOptions<RabbitMqOptions> mqOptions,
        AppDbContext dbContext,
        ILogger<AnalysisService> logger)
    {
        _geminiService = geminiService;
        _emailService = emailService;
        _rabbitMq = rabbitMq;
        _mqOptions = mqOptions.Value;
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

        // Rapor kuyruğuna fire-and-forget — hata raporu kesmez
        _ = PublishReportAsync(analysis);

        sw.Stop();
        _logger.LogInformation("Hasta #{PatientId} analizi tamamlandı ({Elapsed:F3}s)", patientId, sw.Elapsed.TotalSeconds);
        return analysis;
    }

    private async Task PublishReportAsync(PatientAnalysis analysis)
    {
        try
        {
            var payload = new
            {
                analysisId = analysis.Id,
                patientId = analysis.PatientId,
                patientSentence = analysis.PatientSentence,
                mood = analysis.Mood,
                isEmergency = analysis.IsEmergency,
                dailyScore = analysis.DailyScore,
                summary = analysis.Summary,
                analyzedAt = analysis.CreatedAt.ToString("o")
            };
            var json = JsonSerializer.Serialize(payload, _jsonOptions);
            var body = Encoding.UTF8.GetBytes(json);

            await _rabbitMq.PublishAsync(
                queue: _mqOptions.ReportQueue,
                body: body,
                contentType: "application/json");

            _logger.LogInformation("Rapor kuyruğuna gönderildi - Analiz #{AnalysisId}", analysis.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rapor kuyruğuna gönderilemedi - Analiz #{AnalysisId}", analysis.Id);
        }
    }
}
