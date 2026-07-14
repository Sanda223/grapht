using SampleApp.Domain;
using SampleApp.Services;
using SampleApp.Web.Stubs;

namespace SampleApp.Web.Api;

[Route("api/[controller]")]
public class OrdersController
{
    private readonly OrderService _service;

    public OrdersController(OrderService service)
    {
        _service = service;
    }

    [HttpGet("{id}")]
    public Order? GetOrder(int id)
    {
        return _service.Lookup(id);
    }

    [HttpPost]
    public decimal CreateOrder(Order order)
    {
        return _service.PlaceOrder(order);
    }
}
