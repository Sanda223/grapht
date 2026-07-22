// Minimal mimic of the MassTransit surface the extractor detects (namespace-keyed),
// so the fixture exercises the real detection logic without the package dependency.
namespace MassTransit;

public interface IConsumer<in TMessage> where TMessage : class
{
    Task Consume(TMessage message);
}

public interface IPublishEndpoint
{
    Task Publish<TMessage>(TMessage message) where TMessage : class;
}
