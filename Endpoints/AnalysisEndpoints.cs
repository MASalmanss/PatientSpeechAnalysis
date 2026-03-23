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
    }
}
