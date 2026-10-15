namespace TaskApi.Messaging;

public interface IRabbitMqService
{
    void PublishTaskCompleted(TaskItem task);
}
