// Name-based mimics of the ASP.NET Core / SignalR surface the extractor detects,
// so the fixture exercises real detection paths without the framework packages.
namespace SampleApp.Web.Stubs;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RouteAttribute(string template) : Attribute
{
    public string Template { get; } = template;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpGetAttribute(string? template = null) : Attribute
{
    public string? Template { get; } = template;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class HttpPostAttribute : Attribute;

public abstract class Hub
{
    protected IClientProxy Clients { get; } = null!;
}

public interface IClientProxy
{
    Task SendAsync(string method, object? arg);
}

public class HubConnection
{
    public Task InvokeAsync(string methodName, object? arg) => Task.CompletedTask;
    public IDisposable On(string methodName, Action handler) => null!;
}
