namespace PatientSpeechAnalysis.Messaging;

public interface IRabbitMqRpcClient
{
    /// <summary>
    /// Verilen kuyruğa RPC çağrısı yapar. Header'lar ve content-type opsiyonel.
    /// Yanıt body'sini ham byte[] olarak döner. Timeout aşılırsa TimeoutException fırlatır.
    /// </summary>
    Task<RpcResponse> CallAsync(
        string queue,
        byte[] body,
        IDictionary<string, object?>? headers = null,
        string? contentType = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verilen kuyruğa yanıt beklemeden (fire-and-forget) mesaj yayınlar.
    /// </summary>
    Task PublishAsync(
        string queue,
        byte[] body,
        IDictionary<string, object?>? headers = null,
        string? contentType = null,
        CancellationToken cancellationToken = default);
}

public record RpcResponse(byte[] Body, string? ContentType);
