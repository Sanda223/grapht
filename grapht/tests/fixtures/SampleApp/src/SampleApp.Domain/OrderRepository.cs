namespace SampleApp.Domain;

public class OrderRepository : IRepository<Order>
{
    private readonly Dictionary<int, Order> _store = new();

    public Order? Find(int id)
    {
        return _store.TryGetValue(id, out var order) ? order : null;
    }

    public void Save(Order entity)
    {
        _store[entity.Id] = entity;
    }
}
