using MassTransit;
using SampleApp.Domain;

namespace SampleApp.Services;

public class OrderPlacedMessage
{
    public int OrderId { get; set; }
}

public class OrderPlacedConsumer : IConsumer<OrderPlacedMessage>
{
    private readonly IRepository<Order> _repository;

    public OrderPlacedConsumer(IRepository<Order> repository)
    {
        _repository = repository;
    }

    public Task Consume(OrderPlacedMessage message)
    {
        _repository.Find(message.OrderId);
        return Task.CompletedTask;
    }
}

public class OrderPublisher
{
    private readonly IPublishEndpoint _bus;

    public OrderPublisher(IPublishEndpoint bus)
    {
        _bus = bus;
    }

    public Task NotifyPlaced(Order order)
    {
        return _bus.Publish(new OrderPlacedMessage { OrderId = order.Id });
    }
}
