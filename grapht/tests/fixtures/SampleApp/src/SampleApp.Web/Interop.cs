using Microsoft.JSInterop;

namespace SampleApp.Web;

public static class Interop
{
    [JSInvokable("RefreshOrders")]
    public static Task RefreshOrders(int orderId)
    {
        return Task.CompletedTask;
    }
}
