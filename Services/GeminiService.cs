using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly ILogger<GeminiService> _logger;

    public GeminiService(IConfiguration configuration, ILogger<GeminiService> logger)
    {
        _logger = logger;

        var apiKey = configuration["OpenRouter:ApiKey"]
            ?? throw new InvalidOperationException("OpenRouter API key bulunamadı. appsettings.json'da 'OpenRouter:ApiKey' ayarını kontrol edin.");

        _model = configuration["OpenRouter:Model"] ?? "google/gemini-2.0-flash-001";

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://openrouter.ai/api/v1/")
        };
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    }

    public async Task<GeminiAnalysisResult> AnalyzeAsync(string sentence)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Analiz başlatılıyor: \"{Sentence}\"", sentence);

        var prompt = "Sen bir sağlık asistanısın. Yaşlı bir hastadan gelen aşağıdaki cümleyi analiz et.\n\n" +
            "Hasta cümlesi: \"" + sentence + "\"\n\n" +
            "Aşağıdaki JSON formatında yanıt ver. Başka hiçbir metin ekleme, sadece JSON döndür:\n" +
            "{\n" +
            "  \"mood\": \"hastanın duygu durumu (örn: mutlu, üzgün, endişeli, korkmuş, sakin, sinirli, umutsuz, ağrılı)\",\n" +
            "  \"isEmergency\": true/false,\n" +
            "  \"summary\": \"hastanın durumunun kısa özeti (max 250 kelime)\",\n" +
            "  \"dailyScore\": 1-10 arası sağlık puanı\n" +
            "}\n\n" +
            "Acil durum kriterleri (isEmergency = true):\n" +
            "- İntihar düşüncesi veya kendine zarar verme ifadesi\n" +
            "- Şiddetli ağrı, göğüs ağrısı, nefes darlığı\n" +
            "- Düşme, kaza veya yaralanma bildirimi\n" +
            "- Bilinç bulanıklığı, bayılma\n" +
            "- İlaç zehirlenmesi veya yanlış ilaç kullanımı\n" +
            "- Yardım çağrısı veya panik ifadesi\n\n" +
            "DailyScore değerlendirmesi:\n" +
            "- 1-3: Kritik/kötü durum\n" +
            "- 4-5: Endişe verici\n" +
            "- 6-7: Normal/orta\n" +
            "- 8-10: İyi/çok iyi";

        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var apiSw = Stopwatch.StartNew();
            var response = await _httpClient.PostAsync("chat/completions", content);
            apiSw.Stop();
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("OpenRouter API çağrısı: {StatusCode} ({ApiElapsed:F3}s)",
                response.StatusCode, apiSw.Elapsed.TotalSeconds);

            if (!response.IsSuccessStatusCode)
                throw new Exception($"OpenRouter API hatası: {response.StatusCode} - {responseBody}");

            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?? throw new Exception("AI'dan boş yanıt alındı.");

            _logger.LogDebug("Ham yanıt: {RawResponse}", text);

            var cleanedJson = CleanJsonResponse(text);
            var result = JsonSerializer.Deserialize<GeminiAnalysisResult>(cleanedJson)
                ?? throw new Exception("AI yanıtı JSON'a dönüştürülemedi.");

            sw.Stop();
            _logger.LogInformation(
                "Analiz tamamlandı ({Elapsed:F3}s) - Mood: {Mood}, Emergency: {IsEmergency}, Score: {DailyScore}",
                sw.Elapsed.TotalSeconds, result.Mood, result.IsEmergency, result.DailyScore);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Analiz HATA ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            throw;
        }
    }

    private static string CleanJsonResponse(string text)
    {
        var cleaned = text.Trim();

        if (cleaned.StartsWith("```json"))
            cleaned = cleaned[7..];
        else if (cleaned.StartsWith("```"))
            cleaned = cleaned[3..];

        if (cleaned.EndsWith("```"))
            cleaned = cleaned[..^3];

        return cleaned.Trim();
    }
}
