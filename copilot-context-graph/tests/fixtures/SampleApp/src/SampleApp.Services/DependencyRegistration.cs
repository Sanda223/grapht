using SampleApp.Domain;

namespace SampleApp.Services;

// Mimics IServiceCollection registration; detection is method-name based so this
// exercises the same path as real Microsoft.Extensions.DependencyInjection calls.
public static class DependencyRegistration
{
    public static void Register()
    {
        AddScoped<IRepository<Order>, OrderRepository>();
        AddSingleton<OrderService, OrderService>();
    }

    private static void AddScoped<TService, TImplementation>() where TImplementation : TService { }
    private static void AddSingleton<TService, TImplementation>() where TImplementation : TService { }
}
