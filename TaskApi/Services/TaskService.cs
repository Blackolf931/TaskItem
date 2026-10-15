using TaskApi.Contracts;
using TaskApi.Exceptions;
using TaskApi.Messaging;
using TaskApi.Repositories;

namespace TaskApi.Services;

public class TaskService(ITaskRepository repository, IRabbitMqService rabbitMq) : ITaskService
{
    public async Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            Title = request.Title,
            Priority = request.Priority,
            IsCompleted = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repository.AddAsync(task, cancellationToken);

        return task;
    }

    public Task<List<TaskItem>> GetAllAsync(CancellationToken cancellationToken)
    {
        return repository.GetAllAsync(cancellationToken);
    }

    public async Task<TaskItem> CompleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(id, cancellationToken)
                   ?? throw new NotFoundException($"Задача {id} не найдена");

        if (task.IsCompleted)
            throw new ConflictException($"Задача {id} уже завершена");

        task.IsCompleted = true;
        task.CompletedAt = DateTimeOffset.UtcNow;
        await repository.UpdateAsync(task, cancellationToken);

        rabbitMq.PublishTaskCompleted(task);

        return task;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var task = await repository.GetByIdAsync(id, cancellationToken)
                   ?? throw new NotFoundException($"Задача {id} не найдена");

        await repository.DeleteAsync(task, cancellationToken);
    }
}
