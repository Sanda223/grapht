using SampleApp.Web.Stubs;

namespace SampleApp.Web.Api;

public class OrderHub : Hub
{
    public Task BroadcastOrder(int orderId)
    {
        return Clients.SendAsync("OrderChanged", orderId);
    }
}

public class OrderHubClient
{
    private readonly HubConnection _connection = new();

    public void Subscribe()
    {
        _connection.On("OrderChanged", () => { });
    }

    public Task Announce(int orderId)
    {
        return _connection.InvokeAsync("BroadcastOrder", orderId);
    }
}
