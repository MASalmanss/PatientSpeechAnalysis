using PatientSpeechAnalysis.Models;
using PatientSpeechAnalysis.Services;

namespace PatientSpeechAnalysis.Endpoints;

public static class AnalysisEndpoints
{
    public static void MapAnalysisEndpoints(this WebApplication app)
    {
        app.MapPost("/api/analyze", async (AnalysisRequest request, IAnalysisService analysisService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Sentence))
            {
                Console.WriteLine("[API] HATA: Boş cümle gönderildi.");
                return Results.BadRequest(new { error = "Cümle boş olamaz." });
            }

            if (request.PatientId <= 0)
            {
                Console.WriteLine("[API] HATA: Geçersiz hasta ID.");
                return Results.BadRequest(new { error = "Geçerli bir PatientId giriniz." });
            }

            try
            {
                Console.WriteLine($"[API] Yeni analiz isteği - Hasta #{request.PatientId}");
                var result = await analysisService.AnalyzeAsync(request.PatientId, request.Sentence);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] HATA: {ex.Message}");
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
                Console.WriteLine("[API] HATA: Content-Type multipart/form-data değil.");
                return Results.BadRequest(new { error = "İstek multipart/form-data formatında olmalıdır." });
            }

            IFormCollection form;
            try
            {
                form = await httpRequest.ReadFormAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] HATA: Form okunamadı - {ex.Message}");
                return Results.BadRequest(new { error = "Form verisi okunamadı." });
            }

            if (!form.TryGetValue("patientId", out var patientIdValues) ||
                !int.TryParse(patientIdValues.FirstOrDefault(), out var patientId) ||
                patientId <= 0)
            {
                Console.WriteLine("[API] HATA: Geçersiz veya eksik patientId.");
                return Results.BadRequest(new { error = "Geçerli bir patientId gönderiniz." });
            }

            var audioFile = form.Files.GetFile("audio");
            if (audioFile is null || audioFile.Length == 0)
            {
                Console.WriteLine("[API] HATA: Ses dosyası bulunamadı veya boş.");
                return Results.BadRequest(new { error = "'audio' alanında ses dosyası gönderiniz." });
            }

            byte[] audioBytes;
            using (var ms = new MemoryStream())
            {
                await audioFile.CopyToAsync(ms);
                audioBytes = ms.ToArray();
            }

            Console.WriteLine($"[API] Ses analiz isteği - Hasta #{patientId}, Dosya: {audioFile.FileName}, Boyut: {audioBytes.Length} byte");

            try
            {
                var transcribedText = await transcriptionService.TranscribeAsync(audioBytes, audioFile.FileName);

                if (string.IsNullOrWhiteSpace(transcribedText))
                {
                    Console.WriteLine($"[API] UYARI: Hasta #{patientId} için transkript boş döndü.");
                    return Results.UnprocessableEntity(new
                    {
                        error = "Ses dosyasından metin çıkarılamadı. Lütfen daha net bir kayıt deneyin."
                    });
                }

                Console.WriteLine($"[API] Transkript tamamlandı - Hasta #{patientId}: \"{transcribedText}\"");

                var result = await analysisService.AnalyzeAsync(patientId, transcribedText);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("erişilemiyor"))
            {
                Console.WriteLine($"[API] HATA: Transkripsiyon servisi çevrimdışı - {ex.Message}");
                return Results.Json(
                    new { error = "Ses transkripsiyon servisi şu an erişilemiyor.", detail = ex.Message },
                    statusCode: 503);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[API] HATA: {ex.Message}");
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
