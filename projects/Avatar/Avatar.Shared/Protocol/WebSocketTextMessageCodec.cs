using System.Net.WebSockets;
using System.Text;

namespace Avatar.Shared.Protocol;

public static class WebSocketTextMessageCodec
{
	public static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[4096];
		using var payload = new MemoryStream();

		while (true)
		{
			var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				return null;
			}

			if (result.MessageType != WebSocketMessageType.Text)
			{
				continue;
			}

			payload.Write(buffer, 0, result.Count);
			if (result.EndOfMessage)
			{
				break;
			}
		}

		return Encoding.UTF8.GetString(payload.ToArray());
	}

	public static Task SendTextAsync(WebSocket socket, string message, CancellationToken cancellationToken)
	{
		var payload = Encoding.UTF8.GetBytes(message);
		return socket.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
	}
}