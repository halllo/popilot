using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using static ColoredConsole;
using static popilot.AzureDevOps;

namespace popilot.cli.Verbs
{
	[Verb("get-prio")]
	class GetPrioritiesReport
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		[Option('d', longName: "document", Required = false, HelpText = "Generates a document.")]
		public bool GenerateDocument { get; set; }

		[Option('l', longName: "language", Required = false, HelpText = "Generate in language (deutsch by default).")]
		public string? GenerateLanguage { get; set; } = null;

		[Option(longName: "no-ai", Required = false, HelpText = "Does not generate summaries.")]
		public bool NoAi { get; set; }

		public async Task Do(AzureDevOps azureDevOps, IConfiguration config, IAi ai, ILogger<GetPrioritiesReport> logger)
		{
			if (GenerateDocument)
			{
				var (renderer, html) = Renderer.Html();
				await Generate(azureDevOps, config, ai, render: renderer);

				string fileName = $"priorities_{DateTime.Now:yyyyMMdd-HHmmss}.html";
				File.WriteAllText(fileName, html.ToString());
				Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
			}
			else
			{
				await Generate(azureDevOps, config, ai, render: Renderer.Console());
			}
		}

		private async Task Generate(AzureDevOps azureDevOps, IConfiguration config, IAi ai, Renderer render)
		{
			var priorityOrder = (config.GetSection("PriorityOrder").Get<string[]>()?.Reverse() ?? Array.Empty<string>()).ToList();
			var priorities = await azureDevOps.GetPriorities(Project, Team, priorityOrder);
			foreach (var priority in priorities)
			{
				if (priority.WorkItems != null)
				{
					render.PrioHeader(priority);

					if (priority.IsEscalation)
					{
						var escalationWorkItems = priority.WorkItems.Where(w => w.State != "Closed");
						if (escalationWorkItems.Any())
						{
							var summary = NoAi ? string.Empty : await ai.Summarize(escalationWorkItems, Summarizer.ESCALATION + (GenerateLanguage != null ? $" Generiere in {GenerateLanguage}." : string.Empty));
							render.PrioSummary(summary);
						}
					}

					//Relase or Escalaction or any other priority
					var currentSprint = await azureDevOps.GetCurrentIteration(Project, Team);
					{
						var currentSprintWorkItems = priority.WorkItems
							.Where(w => w.IterationPath == currentSprint.Path)
							.ToList();
						if (currentSprintWorkItems.Any())
						{
							var summary = NoAi ? string.Empty : await ai.Summarize(currentSprintWorkItems, Summarizer.SPRINT + (GenerateLanguage != null ? $" Generiere in {GenerateLanguage}." : string.Empty));
							render.PrioSummary(summary);
							render.PrioWorkItems(currentSprintWorkItems);
						}
						else
						{
							render.PrioSprintEmpty();
						}
					}

					if (priority.IsEscalation)
					{
						var multiSprintIteration = string.Join('\\', currentSprint.Path.Split('\\').Reverse().Skip(1).Reverse());
						var inIterationButNotSprint = priority.WorkItems.Where(w => w.IterationPath != currentSprint.Path && w.IterationPath.StartsWith(multiSprintIteration)).ToList();
						if (inIterationButNotSprint.Any())
						{
							var countClosedOrPlanned = priority.WorkItems.Where(w => w.State == "Closed" || w.IterationPath.StartsWith(multiSprintIteration)).Count();
							render.PrioSummary($"Until the end of this iteration we will probably get to {countClosedOrPlanned / (double)priority.Count!:0.0%} ({countClosedOrPlanned}/{priority.Count}).");
						}
					}
				}
			}

			var iterations = await azureDevOps.GetPastIterationsWithCompletedWorkItems(Project, Team, take: 10);
			var sprintStats = iterations.GetSprintStatistics();
			var iterationStats = await azureDevOps.GetIterationStatistics(Project, Team);
			render.Statistics(sprintStats, iterationStats);
			render.End();
		}
	}

	public record Renderer(
		Renderer._Start Start,
		Renderer._PrioHeader PrioHeader,
		Renderer._PrioSummary PrioSummary,
		Renderer._PrioWorkItems PrioWorkItems,
		Renderer._PrioSprintEmpty PrioSprintEmpty,
		Renderer._Statistics Statistics,
		Renderer._End End)
	{
		public delegate void _Start();
		public delegate void _PrioHeader(Priority priority);
		public delegate void _PrioSummary(string summary);
		public delegate void _PrioWorkItems(IEnumerable<IWorkItemDto> workItems);
		public delegate void _PrioSprintEmpty();
		public delegate void _Statistics(Statistics.SprintStatistics sprints, Statistics.IterationStatistics iteration);
		public delegate void _End();

		public static (Renderer, StringBuilder) Html()
		{
			var html = new StringBuilder();
			var renderer = new Renderer(
				Start: () =>
				{
					html.AppendLine("<!html>");
					html.AppendLine("<body>");
				},
				PrioHeader: (priority) =>
				{
					var name = priority.Name;
					var countClosed = priority.CountClosed!;
					var count = priority.Count!;
					var url = priority.Query.UrlHumanReadable();
					html.AppendLine($"""
						<b>{name} {countClosed / (double)count:0.0%}</b>
						<span style="color: gray;">
						(<a href="{url}">{countClosed}/{count}</a>)
						{(priority.IsRelease && priority.Root?.TargetDate != null ? $"planned for {priority.Root.TargetDate.Value.ToLocalTime():dd.MM.yyyy}." : "")}
						</span>
						<br>
					""");
				},
				PrioSummary: (summary) =>
				{
					if (string.IsNullOrWhiteSpace(summary))
					{
						html.AppendLine("");
					}
					else
					{
						html.AppendLine($"{summary}<br><br>");
					}
				},
				PrioWorkItems: (workItems) =>
				{
					var workItemLines = string.Join("<br>", workItems.Select(w =>
					{
						string type = w.Type switch
						{
							"Epic" => Emoji.Known.Crown,
							"Feature" => Emoji.Known.GemStone,
							"Bug" => Emoji.Known.Collision,
							"User Story" => Emoji.Known.PersonInTuxedo,
							"Task" => Emoji.Known.CheckMarkButton,
							_ => w.Type,
						};
						string state = w.State switch
						{
							"Closed" => $"""<span style="color:green;">{w.State}</span>""",
							"Resolved" => $"""<span style="color:cyan;">{w.State}</span>""",
							"Active" => $"""<span style="color:yellow;">{w.State}</span>""",
							_ => $"""<span style="color:gray;">{w.State}</span>""",
						};

						return $"""
						<span>{type}<a href="{w.UrlHumanReadable()}">{w.Id}</a></span>
						<span>{w.Title}</span>
						<span style="color:gray;">[</span>{state}<span style="color:gray;">]</span>
						""";
					}));
					html.AppendLine($"{workItemLines}<br><br>");
				},
				PrioSprintEmpty: () =>
				{
					html.AppendLine($"""In this sprint we work on different topics.<br><br>""");
				},
				Statistics: (sprints, iteration) =>
				{
					html.AppendLine($"""
						<b>Statistics</b>
						<br>
						{sprints.ItemsInLastSprint} closed WorkItems in last sprint (average is {sprints.ItemsPerSprint})
						<br>
						{sprints.StoryPointsInLastSprint} StoryPoints in last sprint (average is {sprints.StoryPointsPerSprint})
						<br>			
						{sprints.LastSprintGoalReached switch
					{
						true => "👍 sprint goal was achieved",
						false => "👎 sprint goal was not achieved",
						_ => ""
					}}
						<br>
						{iteration.FractionClosedWorkItems:0%} closed WorkItems of current iteration
						<br>
						{iteration.FractionCommittedWorkItems:0%} committed WorkItems of current iteration
						<br>
						{iteration.FractionClosedCommittedWorkItems:0%} closed committed WorkItems of current iteration
						<br>
						{iteration.FractionNonRoadmapWork:0%} non-roadmap work
						"""
					);
				},
				End: () =>
				{
					html.AppendLine("</body>");
				}
			);

			return (renderer, html);
		}

		public static Renderer Console()
		{
			return new Renderer(
				Start: () =>
				{
				},
				PrioHeader: (priority) =>
				{
					var countClosed = priority.CountClosed!;
					var count = priority.Count!;
					var rule = new Markup($"{priority.Name} [bold]{countClosed / (double)count:0.0%}[/] [gray]({countClosed}/{count}) {priority.Id}[/]");
					AnsiConsole.Write(rule);
					AnsiConsole.WriteLine();
				},
				PrioSummary: (summary) =>
				{
					AnsiConsole.WriteLine();
				},
				PrioWorkItems: (workItems) =>
				{
					var rows = new Rows(workItems.Select(Display2));
					AnsiConsole.Write(rows);
					AnsiConsole.WriteLine();
					AnsiConsole.WriteLine();
				},
				PrioSprintEmpty: () =>
				{
					AnsiConsole.WriteLine("In diesem Sprint arbeiten wir an anderen Themen.");
					AnsiConsole.WriteLine();
					AnsiConsole.WriteLine();
				},
				Statistics: (sprints, iteration) =>
				{
					Boring("Statistik");
					Info($"{sprints.ItemsInLastSprint} abgeschlossene WorkItems im letzen Sprint (Durchschnitt ist {sprints.ItemsPerSprint})");
					Info($"{sprints.StoryPointsInLastSprint} StoryPoints im letzten Sprint (Durchschnitt ist {sprints.StoryPointsPerSprint})");
				},
				End: () =>
				{
				}
			);
		}
	}
}
