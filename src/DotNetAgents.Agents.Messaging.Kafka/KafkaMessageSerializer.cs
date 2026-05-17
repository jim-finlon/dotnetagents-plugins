using System.Text;
using System.Text.Json;
using DotNetAgents.Agents.Messaging;

namespace DotNetAgents.Agents.Messaging.Kafka;

/// <summary>
/// Serializes and deserializes agent messages for Kafka.
/// </summary>
internal static class KafkaMessageSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes an agent message to bytes for Kafka.
    /// </summary>
    public static byte[] Serialize(AgentMessage message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Deserializes bytes from Kafka to an agent message.
    /// </summary>
    public static AgentMessage? Deserialize(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<AgentMessage>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
