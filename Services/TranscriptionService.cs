using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace PatientSpeechAnalysis.Services;

public class TranscriptionService : ITranscriptionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TranscriptionService> _logger;

    public TranscriptionService(HttpClient httpClient, ILogger<TranscriptionService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(byte[] audioBytes, string fileName)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Ses Python servisine gönderiliyor: {FileName} ({Bytes} byte)",
            fileName, audioBytes.Length);

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var mime = ext switch
        {
            ".wav"  => "audio/wav",
            ".mp3"  => "audio/mpeg",
            ".m4a"  => "audio/mp4",
            ".ogg"  => "audio/ogg",
            ".webm" => "audio/webm",
            _       => "application/octet-stream"
        };

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(audioBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(fileContent, "audio", fileName);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync("transcribe", form);
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Python servisine bağlanılamadı ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException(
                "Ses transkripsiyon servisi erişilemiyor. Python servisinin çalıştığından emin olun.", ex);
        }

        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            sw.Stop();
            _logger.LogError("Python servisi hata döndürdü: {Status} - {Body} ({Elapsed:F3}s)",
                response.StatusCode, body, sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException(
                $"Transkripsiyon servisi hatası: {response.StatusCode} - {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement.GetProperty("text").GetString()
                   ?? throw new InvalidOperationException("Transkripsiyon sonucu boş geldi.");

        sw.Stop();
        _logger.LogInformation("Transkript alındı ({Elapsed:F3}s): \"{Text}\"", sw.Elapsed.TotalSeconds, text);
        return text;
    }
}
