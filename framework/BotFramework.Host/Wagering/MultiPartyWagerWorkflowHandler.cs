using BotFramework.Contracts.Wagering;
using BotFramework.Host.Workflows;

namespace BotFramework.Host.Wagering;

public static class MultiPartyWagerWorkflowHandler
{
    public static Task<MultiPartyWagerResult> Handle(
        MultiPartyWagerWorkflowCommand command,
        MultiPartyWagerWorkflowExecutor executor,
        IDurableWorkflowStepExecutor workflow,
        CancellationToken ct) =>
        workflow.ExecuteAsync(
            command,
            new DurableWorkflowExecutionOptions(
                command.Request.WorkflowId,
                command.CommandId,
                "reserve-play-settle",
                command.Request.WorkflowId),
            () => executor.ExecuteAsync(command, ct),
            static result => result.Accepted,
            static result => result.Terminal,
            static result => result.WorkflowId,
            static result => new { result.Status, result.Outcomes, result.ErrorCode },
            ct);
}
