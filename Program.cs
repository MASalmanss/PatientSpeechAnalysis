using Microsoft.EntityFrameworkCore;
using PatientSpeechAnalysis.Data;
using PatientSpeechAnalysis.Endpoints;
using PatientSpeechAnalysis.Middleware;
using PatientSpeechAnalysis.Services;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("Logs/api-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {Level:u3} | {SourceContext} | {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClient", policy =>
        policy.WithOrigins("http://localhost:5173", "http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

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

builder.Services.AddHttpClient<ITtsService, TtsService>(client =>
{
    var baseUrl = builder.Configuration["TranscriptionService:BaseUrl"] ?? "http://localhost:8000/";
    if (!baseUrl.EndsWith('/')) baseUrl += '/';
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(60);
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    Log.Information("Veritabanı hazır.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowClient");
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseMiddleware<RequestTimingMiddleware>();
app.UseHttpsRedirection();

app.MapAnalysisEndpoints();

Log.Information("AI-Health Patient Speech Analysis API başlatıldı.");
app.Run();
