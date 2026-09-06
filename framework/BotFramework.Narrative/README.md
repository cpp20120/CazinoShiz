# BotFramework.Narrative

`BotFramework.Narrative` contains declarative, transport-independent effects
for games with scenes, dialogs, choices, flags and resumable checkpoints.

Games emit these effects in a `GameDecision`; they do not render Telegram
messages, HTML, buttons or API objects. An adapter implements
`INarrativeEffectSink` and maps the stable `NarrativeAddress`, localized
`NarrativeText` and semantic effect payload to its own UI.

```csharp
var effects = new IGameEffect[]
{
    new SceneEffect(address, "forest", new NarrativeText("scene.forest.title")),
    new DialogEffect(address, new NarrativeText("forest.guide.greeting"), "guide"),
    new ChoiceEffect(address, "crossroads:17",
    [
        new NarrativeChoiceOption("left", new NarrativeText("choice.left")),
        new NarrativeChoiceOption("right", new NarrativeText("choice.right")),
    ]),
};
```

The frontend turns an interaction into `NarrativeChoiceSelection`; the game
module maps that input to its own command. Narrative never dispatches commands
or depends on a particular frontend.

`INarrativeProjectionStore` is the optional read port for resumable narrative
state: currently set flags, the latest checkpoint and an unexpired active
choice. `BotFramework.Narrative.Host` supplies the standard PostgreSQL writer
and reader; a frontend remains responsible only for presenting that state.
