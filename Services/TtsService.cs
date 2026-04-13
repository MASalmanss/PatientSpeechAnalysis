using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PatientSpeechAnalysis.Services;

public class TtsService : ITtsService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TtsService> _logger;

    public TtsService(HttpClient httpClient, ILogger<TtsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<byte[]> SynthesizeAsync(string text)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("TTS isteği gönderiliyor ({CharCount} karakter)", text.Length);

        var requestBody = JsonSerializer.Serialize(new { text });
        using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("tts", content);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Python TTS servisine bağlanılamadı ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException(
                "TTS servisi erişilemiyor. Python servisinin çalıştığından emin olun.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            sw.Stop();
            _logger.LogError("TTS servisi hata döndürdü: {Status} - {Body} ({Elapsed:F3}s)",
                response.StatusCode, errorBody, sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException($"TTS servisi hatası: {response.StatusCode} - {errorBody}");
        }

        var audioBytes = await response.Content.ReadAsByteArrayAsync();
        sw.Stop();
        _logger.LogInformation("TTS ses alındı: {Bytes} byte ({Elapsed:F3}s)", audioBytes.Length, sw.Elapsed.TotalSeconds);

        return audioBytes;
    }
}
