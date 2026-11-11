using System.Diagnostics;
namespace EscapeFromBorderlands.Client;
// Distribution builds intentionally emit no EFB log messages or evaluate their arguments.
internal sealed class ReleaseLog
{
    internal ReleaseLog(object? source = null) { }
    [Conditional("EFB_LOGGING")] internal void LogInfo(object message) { }
    [Conditional("EFB_LOGGING")] internal void LogWarning(object message) { }
    [Conditional("EFB_LOGGING")] internal void LogError(object message) { }
    [Conditional("EFB_LOGGING")] internal void Info(object message) { }
    [Conditional("EFB_LOGGING")] internal void Warning(object message) { }
    [Conditional("EFB_LOGGING")] internal void Error(object message) { }
    [Conditional("EFB_LOGGING")] internal void Success(object message) { }
}
