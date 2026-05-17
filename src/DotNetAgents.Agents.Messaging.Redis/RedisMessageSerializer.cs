using System.Text;
using System.Text.Json;

namespace DotNetAgents.Agents.Messaging.Redis;

/// <summary>
/// Serializes and deserializes agent messages for Redis.
/// </summary>
internal static class RedisMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes an agent message to a string.
    /// </summary>
    public static string Serialize(DotNetAgents.Agents.Messaging.AgentMessage message)
    {
        return JsonSerializer.Serialize(message, Options);
    }

    /// <summary>
    /// Deserializes an agent message from a string.
    /// </summary>
    public static DotNetAgents.Agents.Messaging.AgentMessage? Deserialize(string data)
    {
        try
        {
            return JsonSerializer.Deserialize<DotNetAgents.Agents.Messaging.AgentMessage>(data, Options);
        }
        catch
        {
            return null;
        }
    }
}
