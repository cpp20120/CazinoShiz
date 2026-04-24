using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace BotFramework.Host.Services.RuntimeTuning;

public static class RuntimeTuningMerge
{
    public static T MergeSection<T>(IConfiguration configuration, string sectionPath, JsonNode? patchFragment)
        where T : class, new()
    {
        var baseObj = configuration.GetSection(sectionPath).Get<T>() ?? new T();
        if (patchFragment is not JsonObject patchObj || patchObj.Count == 0)
            return baseObj;

        var baseNode = JsonSerializer.SerializeToNode(baseObj) as JsonObject ?? new JsonObject();
        var merged = JsonObjectMerge.Merge(baseNode, patchObj);
        return JsonSerializer.Deserialize<T>(merged) ?? baseObj;
    }
}
