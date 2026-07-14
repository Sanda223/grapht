# SampleApp architecture

The write path goes through `OrderService`, which persists via `IRepository<Order>`
and publishes an `OrderPlacedMessage` on the bus.

## Messaging

Message contracts and consumers are described in [messaging.md](messaging.md).

## UI

The `OrderCard` component renders a single order and highlights it via JS interop.
