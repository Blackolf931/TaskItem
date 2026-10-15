using Microsoft.EntityFrameworkCore;
using TaskApi.Exceptions;

namespace TaskApi.Repositories;

public class TaskRepository(TaskDbContext db) : ITaskRepository
{
    public async Task<List<TaskItem>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await db.Tasks
            .AsNoTracking()
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task AddAsync(TaskItem task, CancellationToken cancellationToken)
    {
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(TaskItem task, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException($"Задача {task.Id} уже изменена другим запросом");
        }
    }

    public async Task DeleteAsync(TaskItem task, CancellationToken cancellationToken)
    {
        db.Tasks.Remove(task);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await db.Tasks.AnyAsync(t => t.Id == task.Id, cancellationToken))
                throw new NotFoundException($"Задача {task.Id} не найдена");

            throw new ConflictException($"Задача {task.Id} уже изменена другим запросом");
        }
    }
}
