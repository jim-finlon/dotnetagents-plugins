using System.Text;
using System.Text.Json;

namespace DotNetAgents.Agents.Messaging.RabbitMQ;

/// <summary>
/// Serializes and deserializes agent messages for RabbitMQ.
/// </summary>
internal static class RabbitMQMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes an agent message to a byte array.
    /// </summary>
    public static byte[] Serialize(DotNetAgents.Agents.Messaging.AgentMessage message)
    {
        var json = JsonSerializer.Serialize(message, Options);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Deserializes an agent message from a byte array.
    /// </summary>
    public static DotNetAgents.Agents.Messaging.AgentMessage? Deserialize(byte[] data)
    {
        try
        {
            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<DotNetAgents.Agents.Messaging.AgentMessage>(json, Options);
        }
        catch
        {
            return null;
        }
    }
}
