# BotFramework.Presentation

`BotFramework.Presentation` contains transport-independent ordinary UI effects
for games: notifications, structured results, input forms and media. It is
separate from `BotFramework.Narrative`: use narrative for scenes, dialogs and
story choices; use this package for general game UI.

Games emit semantic effects, never Telegram API types, HTML or browser
components. A frontend implements `IPresentationEffectSink` and renders the
stable `PresentationAddress`, `PresentationText` and resource identifiers.

For multiplayer delivery a target may contain a semantic `GameAudience` such as
participants, spectators, a team or a role. The frontend resolves that selector
from its game projection. `RecipientId` and `Audience` cannot be combined:
direct delivery and group visibility are distinct routing choices.

```csharp
var request = new InputRequestEffect(
    requestId: "rename:42",
    expectedPlayerId: "player:7",
    scopeId: "profile:7",
    route: "profile.rename",
    expiresAt: now.AddMinutes(2));

var form = new InputFormEffect(
    new PresentationAddress("profile:7", "player:7"),
    request.RequestId,
    new PresentationText("profile.rename.title"),
    [new PresentationInputField("name", new PresentationText("profile.name"))]);
```

Emit `request` and `form` in the same game decision. The Host persists the
`InputRequestEffect` during the game transaction; the form is delivered only
after commit through the durable effect outbox. A frontend submits a JSON
object as the generic input value, and the game route validates it as untrusted
input. No package in this flow depends on a particular transport.
