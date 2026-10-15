using TaskApi.Contracts;
using TaskApi.Exceptions;
using TaskApi.Services;

namespace TaskApi.Endpoints;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/tasks")
            .AddEndpointFilter<AppExceptionFilter>();

        tasks.MapPost("/", Create)
            .AddEndpointFilter(ValidateCreateRequest)
            .ProducesValidationProblem();

        tasks.MapGet("/", GetAll);

        tasks.MapPut("/{id}/complete", Complete)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        tasks.MapDelete("/{id}", Delete)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async ValueTask<object?> ValidateCreateRequest(EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        if (context.GetArgument<CreateTaskRequest?>(0) is not { } request)
            return Results.Problem("Тело запроса обязательно", statusCode: StatusCodes.Status400BadRequest);

        var errors = request.Validate();
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        return await next(context);
    }

    private static Task<TaskItem> Create(CreateTaskRequest request, ITaskService taskService,
        CancellationToken cancellationToken)
    {
        return taskService.CreateAsync(request, cancellationToken);
    }

    private static Task<List<TaskItem>> GetAll(ITaskService taskService, CancellationToken cancellationToken)
    {
        return taskService.GetAllAsync(cancellationToken);
    }

    private static Task<TaskItem> Complete(Guid id, ITaskService taskService, CancellationToken cancellationToken)
    {
        return taskService.CompleteAsync(id, cancellationToken);
    }

    private static Task Delete(Guid id, ITaskService taskService, CancellationToken cancellationToken)
    {
        return taskService.DeleteAsync(id, cancellationToken);
    }
}
