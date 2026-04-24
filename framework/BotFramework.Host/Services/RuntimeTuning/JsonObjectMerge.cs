using System.Text.Json.Nodes;

namespace BotFramework.Host.Services.RuntimeTuning;

internal static class JsonObjectMerge
{
    /// <summary>Deep-merge <paramref name="patch"/> into <paramref name="baseObj"/> (patch wins). Both must be objects.</summary>
    public static JsonObject Merge(JsonObject baseObj, JsonObject patch)
    {
        var result = (JsonObject?)JsonNode.Parse(baseObj.ToJsonString()) ?? new JsonObject();
        foreach (var prop in patch)
        {
            if (prop.Value is JsonObject patchChild && result[prop.Key] is JsonObject baseChild)
                result[prop.Key] = Merge(baseChild, patchChild);
            else
                result[prop.Key] = prop.Value?.DeepClone();
        }

        return result;
    }
}
