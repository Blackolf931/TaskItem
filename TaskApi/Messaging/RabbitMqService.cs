using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TaskApi.Messaging;

public class RabbitMqService(IOptions<RabbitMqOptions> options, ILogger<RabbitMqService> logger)
    : BackgroundService, IHostedLifecycleService, IRabbitMqService
{
    private const string Exchange = "task.events";
    private const string TaskCompletedRoutingKey = "task.completed";
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinReconnectDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Channel<TaskCompletedMessage> _queue = Channel.CreateBounded<TaskCompletedMessage>(
        new BoundedChannelOptions(1000) { SingleReader = true, FullMode = BoundedChannelFullMode.DropWrite },
        message => logger.LogError("Очередь публикации переполнена, событие {RoutingKey} не отправлено: {Message}",
            TaskCompletedRoutingKey, Serialize(message)));
    
    private readonly CancellationTokenSource _publishCts = new();

    private IConnection? _connection;
    private IChannel? _channel;
    private TimeSpan _reconnectDelay = TimeSpan.Zero;
    private DateTime _nextConnectAttempt = DateTime.MinValue;

    public void PublishTaskCompleted(TaskItem task)
    {
        var message = new TaskCompletedMessage(task.Id, task.Title, task.CompletedAt!.Value.UtcDateTime, task.Priority);

        if (!_queue.Writer.TryWrite(message))
            logger.LogError("Приложение останавливается, событие {RoutingKey} не отправлено: {Message}",
                TaskCompletedRoutingKey, Serialize(message));
    }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in _queue.Reader.ReadAllAsync())
            await PublishAsync(message, _publishCts.Token);
    }

    private async Task PublishAsync(TaskCompletedMessage message, CancellationToken cancellationToken)
    {
        var body = Serialize(message);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            if (channel is null)
            {
                logger.LogError("RabbitMQ недоступен, событие {RoutingKey} не отправлено: {Message}",
                    TaskCompletedRoutingKey, body);
                return;
            }

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent
            };

            await channel.BasicPublishAsync(Exchange, TaskCompletedRoutingKey, mandatory: false, properties,
                Encoding.UTF8.GetBytes(body), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось отправить событие {RoutingKey} в RabbitMQ: {Message}",
                TaskCompletedRoutingKey, body);
            await ResetConnectionAsync();
        }
    }

    private async Task<IChannel?> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        if (DateTime.UtcNow < _nextConnectAttempt)
            return null;

        await ResetConnectionAsync();

        var settings = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName,
            Port = settings.Port,
            UserName = settings.UserName,
            Password = settings.Password,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
        };

        try
        {
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);
            await _channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _reconnectDelay = _reconnectDelay == TimeSpan.Zero
                ? MinReconnectDelay
                : TimeSpan.FromTicks(Math.Min(_reconnectDelay.Ticks * 2, MaxReconnectDelay.Ticks));
            _nextConnectAttempt = DateTime.UtcNow + _reconnectDelay;

            logger.LogWarning(ex, "Не удалось подключиться к RabbitMQ, следующая попытка через {Delay}", _reconnectDelay);
            await ResetConnectionAsync();
            return null;
        }

        _reconnectDelay = TimeSpan.Zero;
        _nextConnectAttempt = DateTime.MinValue;

        return _channel;
    }

    private async Task ResetConnectionAsync()
    {
        try
        {
            if (_channel is not null)
                await _channel.DisposeAsync();
            if (_connection is not null)
                await _connection.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Ошибка при закрытии соединения с RabbitMQ");
        }

        _channel = null;
        _connection = null;
    }

    private async Task CloseConnectionAsync()
    {
        try
        {
            await CloseAndDisposeAsync().WaitAsync(CloseTimeout);
            logger.LogInformation("Соединение с RabbitMQ закрыто");
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Соединение с RabbitMQ не закрылось за {Timeout}, бросаем его", CloseTimeout);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось корректно закрыть соединение с RabbitMQ");
        }
    }

    private async Task CloseAndDisposeAsync()
    {
        if (_channel is { IsOpen: true })
            await _channel.CloseAsync();
        if (_connection is { IsOpen: true })
            await _connection.CloseAsync();

        await ResetConnectionAsync();
    }
    
    public override Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    
    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        _queue.Writer.TryComplete();

        if (ExecuteTask is not null)
        {
            try
            {
                await ExecuteTask.WaitAsync(ShutdownTimeout, cancellationToken);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                logger.LogWarning("Не все события успели отправиться в RabbitMQ за {Timeout}, остальные будут записаны в лог",
                    ShutdownTimeout);
                
                await _publishCts.CancelAsync();
                await ExecuteTask;
            }
        }

        await CloseConnectionAsync();
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override void Dispose()
    {
        _publishCts.Dispose();
        base.Dispose();
    }

    private static string Serialize(TaskCompletedMessage message) => JsonSerializer.Serialize(message, JsonOptions);
}
