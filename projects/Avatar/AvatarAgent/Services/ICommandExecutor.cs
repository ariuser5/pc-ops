using Avatar.Shared.Payloads;

namespace AvatarAgent.Services;

public interface ICommandExecutor
{
	Task<CommandExecutionResult> ExecuteAsync(CommandRequest request, CancellationToken cancellationToken = default);
}

public sealed record CommandExecutionResult(string Action, string Message);