using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avatar.Shared.Protocol;

public static class AvatarProtocolJson
{
	public static JsonSerializerOptions SerializerOptions { get; } = CreateSerializerOptions();

	public static AvatarEnvelope CreateEnvelope<TPayload>(AvatarMessageType type, string? requestId, TPayload? payload)
	{
		JsonElement? payloadElement = null;
		if (payload is not null)
		{
			payloadElement = JsonSerializer.SerializeToElement(payload, SerializerOptions);
		}

		return new AvatarEnvelope
		{
			Type = type.ToProtocolValue(),
			RequestId = requestId,
			Payload = payloadElement
		};
	}

	public static string Serialize(AvatarEnvelope envelope)
	{
		return JsonSerializer.Serialize(envelope, SerializerOptions);
	}

	public static bool TryDeserializeEnvelope(string json, out AvatarEnvelope? envelope, out string error)
	{
		envelope = null;
		error = string.Empty;

		if (string.IsNullOrWhiteSpace(json))
		{
			error = "Message cannot be empty.";
			return false;
		}

		try
		{
			envelope = JsonSerializer.Deserialize<AvatarEnvelope>(json, SerializerOptions);
			if (envelope is null)
			{
				error = "Message payload is missing.";
				return false;
			}

			if (string.IsNullOrWhiteSpace(envelope.Type))
			{
				error = "Envelope type is required.";
				return false;
			}

			return true;
		}
		catch (JsonException exception)
		{
			error = $"Invalid message JSON: {exception.Message}";
			return false;
		}
	}

	public static bool TryDeserializePayload<TPayload>(AvatarEnvelope envelope, out TPayload? payload, out string error)
	{
		payload = default;
		error = string.Empty;

		if (envelope.Payload is null)
		{
			error = "Envelope payload is required.";
			return false;
		}

		try
		{
			payload = envelope.Payload.Value.Deserialize<TPayload>(SerializerOptions);
			if (payload is null)
			{
				error = "Envelope payload is invalid.";
				return false;
			}

			return true;
		}
		catch (JsonException exception)
		{
			error = $"Invalid payload JSON: {exception.Message}";
			return false;
		}
	}

	private static JsonSerializerOptions CreateSerializerOptions()
	{
		return new JsonSerializerOptions(JsonSerializerDefaults.Web)
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			PropertyNameCaseInsensitive = true
		};
	}
}