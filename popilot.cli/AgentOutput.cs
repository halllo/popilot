using AgentDo;
using Spectre.Console;
using System.Text.Json;

namespace popilot.cli
{
	public static class AgentOutput
	{
		public static Events Events(bool streaming = false)
		{
			return new Events
			{
				BeforeMessage = async (role, message) => AnsiConsole.Markup($"[gray]{role}:[/] "),
				OnMessageDelta = async (role, message) => AnsiConsole.Markup(message),
				AfterMessage = async (role, message) => AnsiConsole.MarkupLine(streaming ? string.Empty : $"[gray]{role}:[/] {message}"),
				BeforeToolCall = async (role, tool, toolUse, context, parameters) =>
				{
					AnsiConsole.MarkupLine($"[gray]{role}:[/] [cyan]🛠️{tool.Name}({Markup.Escape(JsonSerializer.Serialize(parameters))})...[/]");
				},
				AfterToolCall = async (role, tool, toolUse, context, result) =>
				{
					AnsiConsole.MarkupLine($"[gray]{toolUse.ToolUseId}: {Markup.Escape(JsonSerializer.Serialize(result))}[/]");
				},
			};
		}
	}
}
