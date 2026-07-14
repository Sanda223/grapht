namespace SampleApp.Web.Api;

public class OrdersApiClient
{
    private readonly HttpClient _http = new();

    public Task<HttpResponseMessage> FetchOrders()
    {
        return _http.GetAsync("api/orders");
    }
}
