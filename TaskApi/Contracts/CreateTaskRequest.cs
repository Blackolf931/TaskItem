namespace TaskApi.Contracts;

public record CreateTaskRequest(string Title, Priority Priority = Priority.Medium)
{
    private const int TitleMaxLength = 200;

    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(Title))
            errors[nameof(Title)] = ["Title не может быть пустым"];
        else if (Title.Length > TitleMaxLength)
            errors[nameof(Title)] = [$"Title не может быть длиннее {TitleMaxLength} символов"];

        if (!Enum.IsDefined(Priority))
            errors[nameof(Priority)] = ["Недопустимое значение Priority"];

        return errors;
    }
}
