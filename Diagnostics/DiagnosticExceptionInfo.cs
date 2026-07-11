namespace ActorMorpher.Diagnostics;

public sealed record DiagnosticExceptionInfo(string Type, string Message, string? StackTrace)
{
    public static DiagnosticExceptionInfo FromException(Exception exception)
        => new(exception.GetType().FullName ?? exception.GetType().Name, exception.Message, exception.StackTrace);
}
