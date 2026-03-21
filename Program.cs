using Microsoft.EntityFrameworkCore;
using PatientSpeechAnalysis.Data;
using PatientSpeechAnalysis.Endpoints;
using PatientSpeechAnalysis.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSingleton<IGeminiService, GeminiService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();

builder.Services.AddHttpClient<ITranscriptionService, TranscriptionService>(client =>
{
    var baseUrl = builder.Configuration["TranscriptionService:BaseUrl"] ?? "http://localhost:8000/";
    if (!baseUrl.EndsWith('/')) baseUrl += '/';
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(
        builder.Configuration.GetValue<int>("TranscriptionService:TimeoutSeconds", 120));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    Console.WriteLine("[Startup] Veritabanı hazır.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapAnalysisEndpoints();

Console.WriteLine("[Startup] AI-Health Patient Speech Analysis API başlatıldı.");
app.Run();
