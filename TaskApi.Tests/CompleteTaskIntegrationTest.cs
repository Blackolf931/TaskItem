using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaskApi.Messaging;

namespace TaskApi.Tests;

public class CompleteTaskIntegrationTest : IAsyncLifetime
{
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly FakeRabbitMqService _rabbitMq = new();

    private WebApplicationFactory<Program> _factory = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<TaskDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<TaskDbContext>>();
                services.AddDbContext<TaskDbContext>(o => o.UseInMemoryDatabase(_databaseName));

                services.RemoveAll<IRabbitMqService>();
                services.AddSingleton<IRabbitMqService>(_rabbitMq);
            });
        });

        return Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteTask_PublishesTaskCompletedMessage()
    {
        var client = _factory.CreateClient();

        var createResponse = await client.PostAsJsonAsync("/tasks", new { title = "Buy milk", priority = "High" });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var taskId = created.GetProperty("id").GetGuid();

        var completeResponse = await client.PutAsync($"/tasks/{taskId}/complete", null);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var completed = await completeResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(completed.GetProperty("isCompleted").GetBoolean());

        var published = Assert.Single(_rabbitMq.Published);
        Assert.Equal(taskId, published.Id);
        Assert.Equal("Buy milk", published.Title);
        Assert.Equal(Priority.High, published.Priority);
        Assert.True(published.IsCompleted);
        Assert.Equal(completed.GetProperty("completedAt").GetDateTimeOffset(), published.CompletedAt);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    private sealed class FakeRabbitMqService : IRabbitMqService
    {
        public ConcurrentQueue<TaskItem> Published { get; } = new();

        public void PublishTaskCompleted(TaskItem task) => Published.Enqueue(task);
    }
}
