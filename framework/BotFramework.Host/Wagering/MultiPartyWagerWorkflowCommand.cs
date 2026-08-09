using BotFramework.Contracts.Wagering;
using BotFramework.Host.Workflows;

namespace BotFramework.Host.Wagering;

public sealed record MultiPartyWagerWorkflowCommand(
    string CommandId,
    MultiPartyWagerRequest Request) : IDurableWorkflowCommand;
