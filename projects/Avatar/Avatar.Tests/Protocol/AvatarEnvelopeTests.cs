using Avatar.Shared.Protocol;

namespace Avatar.Tests.Protocol;

public sealed class AvatarEnvelopeTests
{
	[Theory]
	[InlineData("register", AvatarMessageType.Register)]
	[InlineData("command", AvatarMessageType.Command)]
	[InlineData("result", AvatarMessageType.Result)]
	[InlineData("error", AvatarMessageType.Error)]
	[InlineData("heartbeat", AvatarMessageType.Heartbeat)]
	public void TryGetMessageType_KnownType_ReturnsTrue(string typeString, AvatarMessageType expected)
	{
		var envelope = new AvatarEnvelope { Type = typeString };

		var result = envelope.TryGetMessageType(out var messageType);

		Assert.True(result);
		Assert.Equal(expected, messageType);
	}

	[Theory]
	[InlineData("unknown")]
	[InlineData("cmd")]
	[InlineData("")]
	[InlineData("   ")]
	public void TryGetMessageType_UnknownType_ReturnsFalse(string typeString)
	{
		var envelope = new AvatarEnvelope { Type = typeString };

		var result = envelope.TryGetMessageType(out _);

		Assert.False(result);
	}

	[Theory]
	[InlineData("register", AvatarMessageType.Register)]
	[InlineData("command", AvatarMessageType.Command)]
	public void GetRequiredMessageType_KnownType_ReturnsType(string typeString, AvatarMessageType expected)
	{
		var envelope = new AvatarEnvelope { Type = typeString };

		var messageType = envelope.GetRequiredMessageType();

		Assert.Equal(expected, messageType);
	}

	[Fact]
	public void GetRequiredMessageType_UnknownType_Throws()
	{
		var envelope = new AvatarEnvelope { Type = "unknown-type" };

		Assert.Throws<ArgumentOutOfRangeException>(() => envelope.GetRequiredMessageType());
	}
}
