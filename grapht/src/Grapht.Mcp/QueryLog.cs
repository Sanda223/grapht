using System.Text.Json;

namespace Grapht.Mcp;

/// <summary>Append-only JSONL log of every tool call — the raw material for the
/// token-savings case. One line per call: timestamp, tool, argument summary, result size.
/// Failures to write never break a tool call.</summary>
public sealed class QueryLog
{
    private readonly string _path;
    private readonly Lock _gate = new();

    public QueryLog(string rootDir) =>
        _path = Path.Combine(rootDir, "grapht-out", "querylog.jsonl");

    public string Record(string tool, string args, string result)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var line = JsonSerializer.Serialize(new
            {
                ts = DateTimeOffset.UtcNow.ToString("O"),
                tool,
                args,
                result_chars = result.Length,
            });
            lock (_gate)
                File.AppendAllText(_path, line + Environment.NewLine);
        }
        catch (Exception)
        {
            // Logging is best-effort by design.
        }
        return result;
    }
}
