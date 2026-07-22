namespace SampleApp.Services;

// Dapper-style data access; detection is method-name based on member calls, so this
// stub connection exercises the same path as real Dapper extension methods.
public class OrderQueries
{
    private readonly DbConnectionStub _db = new();

    public Task<IEnumerable<decimal>> Totals()
    {
        return _db.QueryAsync<decimal>("SELECT Total FROM dbo.Orders WHERE Total > 0");
    }

    public Task RecalculateAll()
    {
        return _db.ExecuteAsync("EXEC dbo.GetOrderTotals");
    }

    public Task<IEnumerable<decimal>> TotalsAbove(decimal floor)
    {
        return _db.QueryAsync<decimal>($"SELECT Total FROM dbo.Orders WHERE Total > {floor}");
    }
}

public class DbConnectionStub
{
    public Task<IEnumerable<T>> QueryAsync<T>(string sql) => Task.FromResult(Enumerable.Empty<T>());
    public Task ExecuteAsync(string sql) => Task.CompletedTask;
}
