using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PatientSpeechAnalysis.Messaging;

namespace PatientSpeechAnalysis.Services;

public class TranscriptionService : ITranscriptionService
{
    private readonly IRabbitMqRpcClient _rpc;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<TranscriptionService> _logger;

    public TranscriptionService(
        IRabbitMqRpcClient rpc,
        IOptions<RabbitMqOptions> options,
        ILogger<TranscriptionService> logger)
    {
        _rpc = rpc;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(byte[] audioBytes, string fileName)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Ses kuyruğa gönderiliyor (RabbitMQ): {FileName} ({Bytes} byte)",
            fileName, audioBytes.Length);

        var headers = new Dictionary<string, object?>
        {
            ["x-filename"] = fileName ?? "audio.webm"
        };

        RpcResponse response;
        try
        {
            response = await _rpc.CallAsync(
                queue: _options.WhisperQueue,
                body: audioBytes,
                headers: headers,
                contentType: "application/octet-stream");
        }
        catch (TimeoutException ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Whisper RPC timeout ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException(
                "Ses transkripsiyon servisi erişilemiyor (timeout). Worker çalışıyor mu?", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            sw.Stop();
            _logger.LogError(ex, "Whisper RPC hatası ({Elapsed:F3}s)", sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException(
                "Ses transkripsiyon servisine erişilemiyor.", ex);
        }

        // Worker JSON döndürüyor: {ok:true, result:{text,...}} veya {ok:false, error:"..."}
        using var doc = JsonDocument.Parse(response.Body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
        {
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "bilinmeyen hata";
            sw.Stop();
            _logger.LogError("Worker hata döndürdü: {Error} ({Elapsed:F3}s)", err, sw.Elapsed.TotalSeconds);
            throw new InvalidOperationException($"Transkripsiyon hatası: {err}");
        }

        var text = root.GetProperty("result").GetProperty("text").GetString() ?? "";
        sw.Stop();
        _logger.LogInformation("Transkript alındı ({Elapsed:F3}s): \"{Text}\"", sw.Elapsed.TotalSeconds, text);
        return text;
    }
}
