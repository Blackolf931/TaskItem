using TaskApi.Contracts;

namespace TaskApi.Services;

public interface ITaskService
{
    Task<TaskItem> CreateAsync(CreateTaskRequest request, CancellationToken cancellationToken);
    Task<List<TaskItem>> GetAllAsync(CancellationToken cancellationToken);
    Task<TaskItem> CompleteAsync(Guid id, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
