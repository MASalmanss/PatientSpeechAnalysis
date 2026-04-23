using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PatientSpeechAnalysis.Messaging;

/// <summary>
/// RabbitMQ direct reply-to (amq.rabbitmq.reply-to) kullanan singleton RPC client.
/// Tek bağlantı, tek kanal: publish + reply consumer aynı kanalda yaşar.
/// IChannel thread-safe değil, publish çağrılarını semaphore ile serialize ediyoruz.
/// </summary>
public sealed class RabbitMqRpcClient : IRabbitMqRpcClient, IAsyncDisposable
{
    private const string DirectReplyToQueue = "amq.rabbitmq.reply-to";

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqRpcClient> _logger;
    private readonly SemaphoreSlim _publishLock = new(1, 1);
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RpcResponse>> _pending = new();

    private IConnection? _connection;
    private IChannel? _channel;
    private volatile bool _initialized;
    private volatile bool _disposed;

    public RabbitMqRpcClient(IOptions<RabbitMqOptions> options, ILogger<RabbitMqRpcClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _logger.LogInformation("RabbitMQ bağlantısı kuruluyor: {Host}:{Port}", _options.HostName, _options.Port);
            _connection = await factory.CreateConnectionAsync("psa-backend-rpc", ct);
            _channel = await _connection.CreateChannelAsync(cancellationToken: ct);

            // Reply consumer — direct reply-to pseudo-queue
            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += OnReplyReceivedAsync;
            await _channel.BasicConsumeAsync(
                queue: DirectReplyToQueue,
                autoAck: true,            // direct reply-to zorunlu olarak autoAck
                consumer: consumer,
                cancellationToken: ct);

            _initialized = true;
            _logger.LogInformation("RabbitMQ RPC client hazır.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private Task OnReplyReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var correlationId = ea.BasicProperties.CorrelationId;
        if (string.IsNullOrEmpty(correlationId))
        {
            _logger.LogWarning("Yanıt alındı ama correlation_id yok, atlanıyor.");
            return Task.CompletedTask;
        }

        if (_pending.TryRemove(correlationId, out var tcs))
        {
            var body = ea.Body.ToArray();
            var contentType = ea.BasicProperties.ContentType;
            tcs.TrySetResult(new RpcResponse(body, contentType));
        }
        else
        {
            _logger.LogWarning("Eşleşmeyen correlation_id: {CorrId}", correlationId);
        }

        return Task.CompletedTask;
    }

    public async Task<RpcResponse> CallAsync(
        string queue,
        byte[] body,
        IDictionary<string, object?>? headers = null,
        string? contentType = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(cancellationToken);

        var correlationId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<RpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[correlationId] = tcs;

        var props = new BasicProperties
        {
            CorrelationId = correlationId,
            ReplyTo = DirectReplyToQueue,
            ContentType = contentType
        };
        if (headers is not null && headers.Count > 0)
        {
            props.Headers = new Dictionary<string, object?>(headers);
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(_options.RpcTimeoutSeconds);

        try
        {
            await _publishLock.WaitAsync(cancellationToken);
            try
            {
                await _channel!.BasicPublishAsync(
                    exchange: "",
                    routingKey: queue,
                    mandatory: false,
                    basicProperties: props,
                    body: body,
                    cancellationToken: cancellationToken);
            }
            finally
            {
                _publishLock.Release();
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(effectiveTimeout);

            await using (cts.Token.Register(() =>
            {
                if (_pending.TryRemove(correlationId, out var pendingTcs))
                {
                    if (cancellationToken.IsCancellationRequested)
                        pendingTcs.TrySetCanceled(cancellationToken);
                    else
                        pendingTcs.TrySetException(new TimeoutException(
                            $"RPC çağrısı zaman aşımına uğradı ({effectiveTimeout.TotalSeconds}s) — kuyruk: {queue}"));
                }
            }))
            {
                return await tcs.Task;
            }
        }
        catch
        {
            _pending.TryRemove(correlationId, out _);
            throw;
        }
    }

    public async Task PublishAsync(
        string queue,
        byte[] body,
        IDictionary<string, object?>? headers = null,
        string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(cancellationToken);

        var props = new BasicProperties { ContentType = contentType };
        if (headers is not null && headers.Count > 0)
            props.Headers = new Dictionary<string, object?>(headers);

        await _publishLock.WaitAsync(cancellationToken);
        try
        {
            await _channel!.BasicPublishAsync(
                exchange: "",
                routingKey: queue,
                mandatory: false,
                basicProperties: props,
                body: body,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Mesaj yayınlandı → kuyruk: {Queue}, {Bytes} byte", queue, body.Length);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
            }
            if (_connection is not null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ kapanışında hata.");
        }

        _publishLock.Dispose();
        _initLock.Dispose();
    }
}
