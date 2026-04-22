namespace PatientSpeechAnalysis.Messaging;

public class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string WhisperQueue { get; set; } = "whisper.requests";
    public string TtsQueue { get; set; } = "tts.requests";
    public int RpcTimeoutSeconds { get; set; } = 120;
}
