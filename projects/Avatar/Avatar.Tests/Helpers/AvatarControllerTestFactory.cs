using AvatarController.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Avatar.Tests.Helpers;

public sealed class AvatarControllerTestFactory : WebApplicationFactory<Program>
{
	public IAgentCommandService CommandService { get; } = Substitute.For<IAgentCommandService>();

	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		builder.ConfigureServices(services =>
		{
			// Replace the real command service with the mock
			var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IAgentCommandService));
			if (existing is not null)
			{
				services.Remove(existing);
			}

			services.AddSingleton(CommandService);

			// Remove background hosted services so tests don't run heartbeat timers
			var hostedServices = services
				.Where(d => d.ServiceType == typeof(IHostedService))
				.ToList();

			foreach (var descriptor in hostedServices)
			{
				services.Remove(descriptor);
			}
		});
	}
}
