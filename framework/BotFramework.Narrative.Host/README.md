# BotFramework.Narrative.Host

This package connects `BotFramework.Narrative` effects to the generic Host
effect pipeline. It intentionally contains no Telegram or web implementation.

Implement a sink in the frontend application, then register it at composition:

```csharp
services.AddNarrativeEffectSink<WebNarrativeSink>();
```

The registration adds narrative game-effect handlers, the corresponding runtime
capability provider, and a PostgreSQL `INarrativeProjectionStore`. The framework
projection stores flags, the latest checkpoint and active choice before the sink
is called; durable delivery retries are de-duplicated by delivery id. The sink
therefore owns only rendering and delivery. Use `AddNarrativePersistence()`
when an application only needs the read model and provides its UI elsewhere.
