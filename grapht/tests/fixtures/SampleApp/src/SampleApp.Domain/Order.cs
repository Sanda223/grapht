namespace SampleApp.Domain;

public class Order
{
    public int Id { get; set; }
    public List<OrderLine> Lines { get; } = new();

    public decimal Total()
    {
        return Lines.Sum(line => line.Price * line.Quantity);
    }
}

public class OrderLine
{
    public string Sku { get; set; } = "";
    public decimal Price { get; set; }
    public int Quantity { get; set; }
}
