using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PatientSpeechAnalysis.Messaging;

namespace PatientSpeechAnalysis.Services;

public class TtsService : ITtsService
{
    private readonly IRabbitMqRpcClient _rpc;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<TtsService> _logger;

    public TtsService(
        IRabbitMqRpcClient rpc,
        IOptions<RabbitMqOptions> options,
        ILogger<TtsService> logger)
    {
        _rpc = rpc;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<byte[]> SynthesizeAsync(string text)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("TTS isteği kuyruğa gönderiliyor ({CharCount} karakter)", text.Length);

        var bodyJson = JsonSerializer.Serialize(new { text });
        var bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

        RpcResponse response;
        try
        {
            response = await _rpc.CallAsync(
                queue: _options.TtsQueue,
                body: bodyBytes,
                contentType: "application/json",
                timeout: TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "TTS RPC timeout ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException(
                "TTS servisi erişilemiyor (timeout). Worker çalışıyor mu?", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            sw.Stop();
            _logger.LogError(ex, "TTS RPC hatası ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException("TTS servisine erişilemiyor.", ex);
        }

        // Worker başarılıysa audio/wav döner; hata olursa application/json {ok:false, error}
        if (response.ContentType == "application/json")
        {
            using var doc = JsonDocument.Parse(response.Body);
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : "bilinmeyen hata";
            sw.Stop();
            _logger.LogError("TTS worker hata döndürdü: {Error} ({Elapsed:F3}s)", err, sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException($"TTS hatası: {err}");
        }

        sw.Stop();
        _logger.LogInformation("TTS ses alındı: {Bytes} byte ({Elapsed:F3}s)",
            response.Body.Length, sw.Elapsed.TotalSeconds);
        return response.Body;
    }
}
