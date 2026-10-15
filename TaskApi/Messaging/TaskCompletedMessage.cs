namespace TaskApi.Messaging;

public record TaskCompletedMessage(Guid TaskId, string Title, DateTime CompletedAt, Priority Priority);
