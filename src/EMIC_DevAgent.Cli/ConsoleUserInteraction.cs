using EMIC_DevAgent.Core.Agents.Base;

namespace EMIC_DevAgent.Cli;

public class ConsoleUserInteraction : IUserInteraction
{
    public Task<string> AskQuestionAsync(DisambiguationQuestion question, CancellationToken ct = default)
    {
        Console.WriteLine();
        Console.WriteLine(question.Question);
        for (int i = 0; i < question.Options.Count; i++)
        {
            Console.WriteLine($"  {i + 1}. {question.Options[i]}");
        }
        Console.Write("Seleccione una opcion > ");

        var input = Console.ReadLine()?.Trim() ?? string.Empty;

        if (int.TryParse(input, out var index) && index >= 1 && index <= question.Options.Count)
        {
            return Task.FromResult(question.Options[index - 1]);
        }

        return Task.FromResult(input);
    }

    public Task ReportProgressAsync(string agentName, string message, double? progressPercent = null, CancellationToken ct = default)
    {
        var progress = progressPercent.HasValue ? $" ({progressPercent:F0}%)" : string.Empty;
        Console.WriteLine($"[{agentName}]{progress} {message}");
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmActionAsync(string description, CancellationToken ct = default)
    {
        Console.Write($"{description} (s/n) > ");
        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        return Task.FromResult(input == "s" || input == "si" || input == "y" || input == "yes");
    }
}
