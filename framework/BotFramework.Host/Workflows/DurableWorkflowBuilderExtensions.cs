using System.Reflection;
using Microsoft.Extensions.Configuration;
using BotFramework.Host.Workflows;
using BotFramework.Host.Wagering;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;

namespace BotFramework.Host.Composition.Builder;

public static class DurableWorkflowBuilderExtensions
{
    /// <summary>
    /// Adds the framework-owned durable workflow infrastructure. Application
    /// assemblies only provide immutable command records and Wolverine
    /// handlers; storage, retries, replay and generic saga state stay here.
    /// The PostgreSQL store belongs to the current service. Cross-service
    /// coordination must use contracts/outbox or a remote transport, not a
    /// shared domain database.
    /// </summary>
    public static IHostApplicationBuilder AddDurableWorkflows(
        this IHostApplicationBuilder builder,
        params Assembly[] handlerAssemblies)
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required for durable workflows.");
        var schema = builder.Configuration["DurableWorkflow:Schema"] ?? "durable_workflow";
        var configuredDurabilityMode = builder.Configuration["DurableWorkflow:Mode"];
        var autoCreateMessageStore = builder.Configuration.GetValue<bool>("DurableWorkflow:AutoCreate");

        builder.Services.AddSingleton<IDurableWorkflowStepStore, PostgresDurableWorkflowStepStore>();
        builder.Services.AddSingleton<IDurableWorkflowTimeoutStore, PostgresDurableWorkflowTimeoutStore>();
        builder.Services.AddSingleton<IDurableWorkflowRecoveryService, PostgresDurableWorkflowRecoveryService>();
        builder.Services.AddScoped<IDurableWorkflowDispatcher, DurableWorkflowDispatcher>();
        builder.Services.AddScoped<IDurableWorkflowStepExecutor, DurableWorkflowStepExecutor>();
        builder.Services.AddScoped<IDurableWorkflowReplayService, DurableWorkflowReplayService>();
        builder.Services.AddHostedService<DurableWorkflowTimeoutDispatcher>();

        builder.UseWolverine(opts =>
        {
            var persistence = opts.PersistMessagesWithPostgresql(connectionString, schema);
            if (autoCreateMessageStore)
                persistence.OverrideAutoCreateResources(JasperFx.AutoCreate.CreateOrUpdate);
            if (Enum.TryParse<DurabilityMode>(configuredDurabilityMode, ignoreCase: true, out var durabilityMode))
                opts.Durability.Mode = durabilityMode;
            opts.Policies.UseDurableLocalQueues();
            opts.Policies.OnException<Exception>().ScheduleRetryIndefinitely(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5));
            opts.MessagePartitioning.UseInferredMessageGrouping();
            opts.Discovery.IncludeAssembly(typeof(DurableWorkflowStepHandler).Assembly);
            // The multi-party saga deliberately depends on the scoped command
            // publisher, whose Local implementation resolves handlers through
            // IServiceProvider. Keep Wolverine codegen strict everywhere else,
            // but let this workflow use the scoped service location boundary.
            opts.CodeGeneration.AlwaysUseServiceLocationFor(typeof(MultiPartyWagerWorkflowExecutor));
            foreach (var assembly in handlerAssemblies.Where(static assembly => assembly is not null).Distinct())
            {
                opts.Discovery.IncludeAssembly(assembly);
                foreach (var type in assembly.GetExportedTypes()
                             .Where(static type => type.Name.EndsWith("CommandExecutor", StringComparison.Ordinal)))
                    opts.CodeGeneration.AlwaysUseServiceLocationFor(type);
            }
        });

        return builder;
    }
}
