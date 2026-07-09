using Avatar.RemoteControl.Services;

namespace Avatar.RemoteControl;

public class InteractiveShell
{
    private readonly ControllerClient _client;

    public InteractiveShell(ControllerClient client)
    {
        _client = client;
    }

    public async Task RunAsync()
    {
        Console.WriteLine("╔════════════════════════════════════════╗");
        Console.WriteLine("║   Avatar Remote Control - Test CLI    ║");
        Console.WriteLine("╚════════════════════════════════════════╝\n");

        bool running = true;
        while (running)
        {
            running = await ShowAgentSelectionMenuAsync();
        }

        Console.WriteLine("\n✓ Exiting Avatar Remote Control.");
    }

    private async Task<bool> ShowAgentSelectionMenuAsync()
    {
        Console.WriteLine("\n[Select Agent]\n");

        var agents = await _client.GetConnectedAgentsAsync();
        if (agents.Count == 0)
        {
            Console.WriteLine("  ✗ No agents connected. Make sure AvatarController is running.");
            Console.Write("  Retry? (y/n): ");
            return Console.ReadLine()?.ToLower() == "y";
        }

        for (int i = 0; i < agents.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {agents[i].AgentId} ({agents[i].AgentName ?? "unnamed"})");
        }

        Console.WriteLine($"  {agents.Count + 1}. Exit");
        Console.Write("\n  Choose: ");

        if (!int.TryParse(Console.ReadLine(), out int choice) || choice < 1 || choice > agents.Count + 1)
        {
            Console.WriteLine("  ✗ Invalid choice.");
            return true;
        }

        if (choice == agents.Count + 1)
            return false;

        var selectedAgent = agents[choice - 1];
        await ShowControlMenuAsync(selectedAgent);
        return true;
    }

    private async Task ShowControlMenuAsync(AgentSummary agent)
    {
        Console.WriteLine($"\n[Controlling {agent.AgentId}]\n");

        bool controlling = true;
        while (controlling)
        {
            Console.WriteLine("  Commands:");
            Console.WriteLine("    1. Move Mouse");
            Console.WriteLine("    2. Click");
            Console.WriteLine("    3. Send Key");
            Console.WriteLine("    4. Back to Agent Selection");
            Console.Write("\n  Choose: ");

            var choice = Console.ReadLine()?.Trim();

            switch (choice)
            {
                case "1":
                    await HandleMoveMouseAsync(agent.AgentId);
                    break;
                case "2":
                    if (await _client.SendCommandAsync(agent.AgentId, "LeftClick"))
                        Console.WriteLine("  ✓ Click sent.");
                    else
                        Console.WriteLine("  ✗ Failed to send click.");
                    break;
                case "3":
                    await HandleSendKeyAsync(agent.AgentId);
                    break;
                case "4":
                    controlling = false;
                    break;
                default:
                    Console.WriteLine("  ✗ Invalid choice.");
                    break;
            }
        }
    }

    private async Task HandleMoveMouseAsync(string agentId)
    {
        Console.Write("    X coordinate: ");
        if (!int.TryParse(Console.ReadLine(), out int x))
        {
            Console.WriteLine("    ✗ Invalid X coordinate.");
            return;
        }

        Console.Write("    Y coordinate: ");
        if (!int.TryParse(Console.ReadLine(), out int y))
        {
            Console.WriteLine("    ✗ Invalid Y coordinate.");
            return;
        }

        if (await _client.SendCommandAsync(agentId, "MoveMouse", x, y))
            Console.WriteLine($"    ✓ Mouse moved to ({x}, {y}).");
        else
            Console.WriteLine("    ✗ Failed to move mouse.");
    }

    private async Task HandleSendKeyAsync(string agentId)
    {
        Console.Write("    Key name (e.g., Enter, Space, A): ");
        var key = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(key))
        {
            Console.WriteLine("    ✗ Key cannot be empty.");
            return;
        }

        if (await _client.SendCommandAsync(agentId, "SendKey", key: key))
            Console.WriteLine($"    ✓ Key '{key}' sent.");
        else
            Console.WriteLine("    ✗ Failed to send key.");
    }
}
