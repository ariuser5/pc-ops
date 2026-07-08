using Avatar.Shared.Payloads;
using Avatar.Shared.Protocol;

namespace Avatar.Tests.Protocol;

public sealed class AvatarProtocolJsonTests
{
	[Fact]
	public void TryDeserializeEnvelope_ValidJson_ReturnsEnvelope()
	{
		var json = """{"type":"command","requestId":"abc123","payload":{"action":"MoveMouse","x":10,"y":20}}""";

		var result = AvatarProtocolJson.TryDeserializeEnvelope(json, out var envelope, out var error);

		Assert.True(result);
		Assert.NotNull(envelope);
		Assert.Equal("command", envelope.Type);
		Assert.Equal("abc123", envelope.RequestId);
		Assert.Empty(error);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	public void TryDeserializeEnvelope_EmptyOrNull_ReturnsFalse(string? json)
	{
		var result = AvatarProtocolJson.TryDeserializeEnvelope(json!, out var envelope, out var error);

		Assert.False(result);
		Assert.Null(envelope);
		Assert.NotEmpty(error);
	}

	[Fact]
	public void TryDeserializeEnvelope_InvalidJson_ReturnsFalse()
	{
		var result = AvatarProtocolJson.TryDeserializeEnvelope("not-json{", out var envelope, out var error);

		Assert.False(result);
		Assert.Null(envelope);
		Assert.NotEmpty(error);
	}

	[Fact]
	public void TryDeserializeEnvelope_MissingType_ReturnsFalse()
	{
		var json = """{"requestId":"abc123","payload":{}}""";

		var result = AvatarProtocolJson.TryDeserializeEnvelope(json, out var envelope, out var error);

		Assert.False(result);
		Assert.NotEmpty(error);
	}

	[Fact]
	public void TryDeserializePayload_ValidPayload_ReturnsPayload()
	{
		var json = """{"type":"command","requestId":"r1","payload":{"action":"MoveMouse","x":5,"y":10}}""";
		AvatarProtocolJson.TryDeserializeEnvelope(json, out var envelope, out _);

		var result = AvatarProtocolJson.TryDeserializePayload<CommandRequest>(envelope!, out var payload, out var error);

		Assert.True(result);
		Assert.NotNull(payload);
		Assert.Equal("MoveMouse", payload.Action);
		Assert.Equal(5, payload.X);
		Assert.Equal(10, payload.Y);
		Assert.Empty(error);
	}

	[Fact]
	public void TryDeserializePayload_NullPayload_ReturnsFalse()
	{
		var envelope = new AvatarEnvelope { Type = "command", Payload = null };

		var result = AvatarProtocolJson.TryDeserializePayload<CommandRequest>(envelope, out var payload, out var error);

		Assert.False(result);
		Assert.Null(payload);
		Assert.NotEmpty(error);
	}

	[Fact]
	public void CreateEnvelope_WithPayload_SerializesCorrectly()
	{
		var command = new CommandRequest { Action = "MoveMouse", X = 10, Y = 20 };

		var envelope = AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Command, "req1", command);

		Assert.Equal("command", envelope.Type);
		Assert.Equal("req1", envelope.RequestId);
		Assert.NotNull(envelope.Payload);
	}

	[Fact]
	public void CreateEnvelope_WithNullPayload_PayloadIsNull()
	{
		var envelope = AvatarProtocolJson.CreateEnvelope<object?>(AvatarMessageType.Heartbeat, "hb1", null);

		Assert.Equal("heartbeat", envelope.Type);
		Assert.Null(envelope.Payload);
	}

	[Fact]
	public void Serialize_RoundTrip_PreservesFields()
	{
		var command = new CommandRequest { Action = "MoveMouse", X = 42, Y = 99 };
		var original = AvatarProtocolJson.CreateEnvelope(AvatarMessageType.Command, "round-trip-id", command);

		var json = AvatarProtocolJson.Serialize(original);
		AvatarProtocolJson.TryDeserializeEnvelope(json, out var deserialized, out _);
		AvatarProtocolJson.TryDeserializePayload<CommandRequest>(deserialized!, out var payload, out _);

		Assert.Equal(original.Type, deserialized!.Type);
		Assert.Equal(original.RequestId, deserialized.RequestId);
		Assert.Equal(42, payload!.X);
		Assert.Equal(99, payload.Y);
	}

	[Fact]
	public void Serialize_OmitsNullFields()
	{
		var envelope = AvatarProtocolJson.CreateEnvelope<object?>(AvatarMessageType.Heartbeat, null, null);

		var json = AvatarProtocolJson.Serialize(envelope);

		Assert.DoesNotContain("requestId", json, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
	}
}
