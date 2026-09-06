# BotFramework.Presentation.Host

This package connects `BotFramework.Presentation` effects to the generic Host
effect pipeline. It contains no Telegram, web or game implementation.

Implement a frontend sink, then register it in the composition root:

```csharp
services.AddPresentationEffectSink<WebPresentationSink>();
```

The registration adds the generic effect handler and the capabilities for
ordinary messages, rich results, input forms and media. Every presentation
effect implements `IDurableGameEffect`, so the Host places it in the existing
transactional outbox and invokes the sink only after the game transaction
commits. Sinks should use `PresentationDeliveryContext.DeliveryId` for
idempotency on at-least-once retries.
