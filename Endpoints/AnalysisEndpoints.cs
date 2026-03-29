using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PatientSpeechAnalysis.Models;
using PatientSpeechAnalysis.Services;

namespace PatientSpeechAnalysis.Endpoints;

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AnalysisEndpoints");

        app.MapPost("/api/analyze", async (AnalysisRequest request, IAnalysisService analysisService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Sentence))
            {
                logger.LogWarning("Boş cümle gönderildi.");
                return Results.BadRequest(new { error = "Cümle boş olamaz." });
            }

            if (request.PatientId <= 0)
            {
                logger.LogWarning("Geçersiz hasta ID: {PatientId}", request.PatientId);
                return Results.BadRequest(new { error = "Geçerli bir PatientId giriniz." });
            }

            try
            {
                logger.LogInformation("Yeni analiz isteği - Hasta #{PatientId}", request.PatientId);
                var result = await analysisService.AnalyzeAsync(request.PatientId, request.Sentence);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Analiz hatası - Hasta #{PatientId}", request.PatientId);
                return Results.Json(new { error = "Analiz sırasında bir hata oluştu.", detail = ex.Message }, statusCode: 500);
            }
        })
        .WithName("AnalyzePatientSpeech")
        .WithOpenApi()
        .Produces<PatientAnalysis>(200)
        .Produces(400)
        .Produces(500);

        app.MapPost("/api/analyze/audio", async (
            HttpRequest httpRequest,
            ITranscriptionService transcriptionService,
            IAnalysisService analysisService) =>
        {
            if (!httpRequest.HasFormContentType)
            {
                logger.LogWarning("Content-Type multipart/form-data değil.");
                return Results.BadRequest(new { error = "İstek multipart/form-data formatında olmalıdır." });
            }

            IFormCollection form;
            try
            {
                form = await httpRequest.ReadFormAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Form okunamadı.");
                return Results.BadRequest(new { error = "Form verisi okunamadı." });
            }

            if (!form.TryGetValue("patientId", out var patientIdValues) ||
                !int.TryParse(patientIdValues.FirstOrDefault(), out var patientId) ||
                patientId <= 0)
            {
                logger.LogWarning("Geçersiz veya eksik patientId.");
                return Results.BadRequest(new { error = "Geçerli bir patientId gönderiniz." });
            }

            var audioFile = form.Files.GetFile("audio");
            if (audioFile is null || audioFile.Length == 0)
            {
                logger.LogWarning("Ses dosyası bulunamadı veya boş.");
                return Results.BadRequest(new { error = "'audio' alanında ses dosyası gönderiniz." });
            }

            byte[] audioBytes;
            using (var ms = new MemoryStream())
            {
                await audioFile.CopyToAsync(ms);
                audioBytes = ms.ToArray();
            }

            logger.LogInformation("Ses analiz isteği - Hasta #{PatientId}, Dosya: {FileName}, Boyut: {Size} byte",
                patientId, audioFile.FileName, audioBytes.Length);

            try
            {
                var transcribedText = await transcriptionService.TranscribeAsync(audioBytes, audioFile.FileName);

                if (string.IsNullOrWhiteSpace(transcribedText))
                {
                    logger.LogWarning("Hasta #{PatientId} için transkript boş döndü.", patientId);
                    return Results.UnprocessableEntity(new
                    {
                        error = "Ses dosyasından metin çıkarılamadı. Lütfen daha net bir kayıt deneyin."
                    });
                }

                logger.LogInformation("Transkript tamamlandı - Hasta #{PatientId}: \"{TranscribedText}\"",
                    patientId, transcribedText);

                var result = await analysisService.AnalyzeAsync(patientId, transcribedText);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("erişilemiyor"))
            {
                logger.LogError(ex, "Transkripsiyon servisi çevrimdışı.");
                return Results.Json(
                    new { error = "Ses transkripsiyon servisi şu an erişilemiyor.", detail = ex.Message },
                    statusCode: 503);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Ses analizi hatası - Hasta #{PatientId}", patientId);
                return Results.Json(
                    new { error = "Ses analizi sırasında bir hata oluştu.", detail = ex.Message },
                    statusCode: 500);
            }
        })
        .WithName("AnalyzePatientAudio")
        .WithOpenApi()
        .Produces<PatientAnalysis>(200)
        .Produces(400)
        .Produces(422)
        .Produces(503)
        .Produces(500);

        app.Map("/ws/analyze", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("WebSocket bağlantısı gereklidir.");
                return;
            }

            if (!context.Request.Query.TryGetValue("patientId", out var pidValues) ||
                !int.TryParse(pidValues.FirstOrDefault(), out var patientId) ||
                patientId <= 0)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Geçersiz veya eksik patientId.");
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            logger.LogInformation("WS bağlantısı alındı - Hasta #{PatientId}", patientId);

            using var audioBuffer = new MemoryStream();
            var recvBuffer = new byte[8192];

            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(recvBuffer), CancellationToken.None);
                }
                catch (WebSocketException ex)
                {
                    logger.LogWarning(ex, "WS bağlantısı beklenmedik şekilde kapandı - Hasta #{PatientId}", patientId);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Kapatıldı", CancellationToken.None);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await audioBuffer.WriteAsync(recvBuffer.AsMemory(0, result.Count));
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = Encoding.UTF8.GetString(recvBuffer, 0, result.Count);
                    if (text == "START")
                    {
                        logger.LogInformation("WS kayıt başladı - Hasta #{PatientId}", patientId);
                    }
                    else if (text == "END")
                    {
                        break;
                    }
                }
            }

            if (audioBuffer.Length == 0)
            {
                logger.LogWarning("WS boş ses - Hasta #{PatientId}", patientId);
                await SendTextAsync(ws, "ERROR:Ses verisi alınamadı.", CancellationToken.None);
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Boş ses", CancellationToken.None);
                return;
            }

            var audioBytes = audioBuffer.ToArray();
            logger.LogInformation("WS ses alındı - Hasta #{PatientId}, {Bytes} byte", patientId, audioBytes.Length);

            using var scope = context.RequestServices.CreateScope();
            var transcriptionService = scope.ServiceProvider.GetRequiredService<ITranscriptionService>();
            var analysisService = scope.ServiceProvider.GetRequiredService<IAnalysisService>();

            string transcribedText;
            try
            {
                transcribedText = await transcriptionService.TranscribeAsync(audioBytes, "recording.webm");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("erişilemiyor"))
            {
                logger.LogError(ex, "WS transkripsiyon servisi çevrimdışı - Hasta #{PatientId}", patientId);
                await SendTextAsync(ws, "ERROR:Transkripsiyon servisi erişilemiyor.", CancellationToken.None);
                await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "Servis hatası", CancellationToken.None);
                return;
            }

            if (string.IsNullOrWhiteSpace(transcribedText))
            {
                logger.LogWarning("WS boş transkript - Hasta #{PatientId}", patientId);
                await SendTextAsync(ws, "ERROR:Ses dosyasından metin çıkarılamadı.", CancellationToken.None);
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Boş transkript", CancellationToken.None);
                return;
            }

            logger.LogInformation("WS transkript - Hasta #{PatientId}: \"{Text}\"", patientId, transcribedText);

            PatientAnalysis analysisResult;
            try
            {
                analysisResult = await analysisService.AnalyzeAsync(patientId, transcribedText);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "WS analiz hatası - Hasta #{PatientId}", patientId);
                await SendTextAsync(ws, $"ERROR:Analiz hatası: {ex.Message}", CancellationToken.None);
                await ws.CloseAsync(WebSocketCloseStatus.InternalServerError, "Analiz hatası", CancellationToken.None);
                return;
            }

            var json = JsonSerializer.Serialize(analysisResult, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await SendTextAsync(ws, json, CancellationToken.None);
            logger.LogInformation("WS sonuç gönderildi - Hasta #{PatientId}, ID: {AnalysisId}", patientId, analysisResult.Id);
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Tamamlandı", CancellationToken.None);
        });
    }

    private static async Task SendTextAsync(WebSocket ws, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken: ct);
    }
}
