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
    }
}
