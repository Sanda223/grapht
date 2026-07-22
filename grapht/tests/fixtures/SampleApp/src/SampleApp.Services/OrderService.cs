using SampleApp.Domain;

namespace SampleApp.Services;

public class OrderService
{
    private readonly IRepository<Order> _repository;

    public OrderService(IRepository<Order> repository)
    {
        _repository = repository;
    }

    public decimal PlaceOrder(Order order)
    {
        var total = order.Total();
        var discounted = PriceCalculator.ApplyDiscount(total, order.Lines.Count);
        _repository.Save(order);
        return discounted;
    }

    public Order? Lookup(int id)
    {
        return _repository.Find(id);
    }
}
