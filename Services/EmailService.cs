using MailKit.Net.Smtp;
using MimeKit;
using PatientSpeechAnalysis.Models;

namespace PatientSpeechAnalysis.Services;

public class EmailService : IEmailService
{
    private readonly IConfiguration _configuration;

    public EmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmergencyEmailAsync(PatientAnalysis analysis)
    {
        var smtpHost = _configuration["Email:SmtpHost"];
        var smtpPort = int.TryParse(_configuration["Email:SmtpPort"], out var port) ? port : 587;
        var username = _configuration["Email:Username"];
        var password = _configuration["Email:Password"];
        var from = _configuration["Email:From"];
        var to = _configuration["Email:To"];
        var cc = _configuration["Email:Cc"];

        if (string.IsNullOrEmpty(smtpHost) || string.IsNullOrEmpty(username) ||
            string.IsNullOrEmpty(password) || username == "TODO" || password == "TODO")
        {
            Console.WriteLine("[EmailService] UYARI: SMTP ayarları eksik veya yapılandırılmamış. E-posta gönderilemedi.");
            Console.WriteLine("[EmailService] Acil durum e-postası gönderilecekti:");
            Console.WriteLine($"  Hasta #{analysis.PatientId} - {analysis.Mood}");
            Console.WriteLine($"  Cümle: {analysis.PatientSentence}");
            Console.WriteLine($"  Özet: {analysis.Summary}");
            return;
        }

        Console.WriteLine($"[EmailService] Acil durum e-postası gönderiliyor - Hasta #{analysis.PatientId}...");

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(from));
            message.To.Add(MailboxAddress.Parse(to));

            if (!string.IsNullOrEmpty(cc))
                message.Cc.Add(MailboxAddress.Parse(cc));

            message.Subject = $"ACİL DURUM BİLDİRİMİ - Hasta #{analysis.PatientId}";

            message.Body = new TextPart("html")
            {
                Text = $"""
                    <h2 style="color: red;">⚠️ ACİL DURUM BİLDİRİMİ</h2>
                    <hr/>
                    <p><strong>Hasta ID:</strong> {analysis.PatientId}</p>
                    <p><strong>Tarih:</strong> {analysis.CreatedAt:dd.MM.yyyy HH:mm:ss} UTC</p>
                    <p><strong>Duygu Durumu:</strong> {analysis.Mood}</p>
                    <p><strong>Günlük Skor:</strong> {analysis.DailyScore}/10</p>
                    <hr/>
                    <h3>Hastanın Cümlesi:</h3>
                    <blockquote style="border-left: 3px solid red; padding-left: 10px; color: #333;">
                        {analysis.PatientSentence}
                    </blockquote>
                    <h3>AI Analiz Özeti:</h3>
                    <p>{analysis.Summary}</p>
                    <hr/>
                    <p style="color: gray; font-size: 12px;">Bu e-posta AI-Health Patient Speech Analysis sistemi tarafından otomatik gönderilmiştir.</p>
                    """
            };

            using var client = new SmtpClient();
            await client.ConnectAsync(smtpHost, smtpPort, MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(username, password);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Console.WriteLine($"[EmailService] E-posta başarıyla gönderildi - Hasta #{analysis.PatientId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailService] E-posta gönderim HATASI: {ex.Message}");
        }
    }
}
