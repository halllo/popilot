using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using static ColoredConsole;

namespace popilot.cli.Verbs
{
	[Verb("get-sprints")]
	class GetSprints
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetSprints> logger)
		{
			var sprints = await azureDevOps.GetIterations(Project, Team);
			foreach (var sprint in sprints)
			{
				Info(sprint.Path, sameLine: true);
				Boring($" {sprint.Attributes.TimeFrame}; from {sprint.Attributes.StartDate:dd.MM.yyyy} to {sprint.Attributes.FinishDate:dd.MM.yyyy}");
			}
			logger.LogInformation("{Sprints} loaded", sprints.Count);
		}
	}

	[Verb("get-all-sprints")]
	class GetAllSprints
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetAllSprints> logger)
		{
			var sprints = await azureDevOps.GetAllIterations(Project, Team);

			foreach (var sprint in sprints)
			{
				Info(sprint.Path, sameLine: true);
				Boring($" {sprint.Attributes.TimeFrame}; from {sprint.Attributes.StartDate:dd.MM.yyyy} to {sprint.Attributes.FinishDate:dd.MM.yyyy}");
			}
			logger.LogInformation("{Sprints} loaded", sprints.Count);
		}
	}

	[Verb("get-current-sprint")]
	class GetCurrentSprint
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }
		[Option('o', longName: "output", Required = false)]
		public string? Output { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetCurrentSprint> logger)
		{
			var currentSprint = await azureDevOps.GetCurrentIteration(Project, Team);
			logger.LogInformation("Sprint {Name} from {Start:dd.MM.yyyy} to {Finish:dd.MM.yyyy}", currentSprint.Name, currentSprint.Attributes.StartDate, currentSprint.Attributes.FinishDate);

			var workItems = (await azureDevOps.GetWorkItems(currentSprint))
				.OrderBy(i => i.StackRank)
				.Where(i => new[] { "Bug", "User Story" }.Contains(i.Type))
				.ToList();

			var workItemsProjected = workItems.Select(i => new
			{
				i.Id,
				i.Title,
				i.State,
				Topic = i.ParentTitle,
				i.Tags,
			});

			switch (Output)
			{
				case "json":
					{
						Boring(Json(workItemsProjected, indented: false));
						break;
					}
				case "json+pretty":
					{
						Boring(Json(workItemsProjected, indented: true));
						break;
					}
				default:
					{
						foreach (var workItem in workItems)
						{
							Display(workItem);
						}
						break;
					}
			}
		}
	}

	[Verb("get-sprint")]
	class GetSprint
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }
		[Option('o', longName: "output", Required = false)]
		public string? Output { get; set; }

		[Value(0, MetaName = "sprint path", Required = true)]
		public string Path { get; set; } = null!;

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetSprint> logger)
		{
			var sprint = await azureDevOps.GetIteration(Path, Project, Team);
			if (sprint == null)
			{
				logger.LogError("Sprint {Path} not found.", Path);
				return;
			}

			logger.LogInformation("Sprint {Name} from {Start:dd.MM.yyyy} to {Finish:dd.MM.yyyy}", sprint.Name, sprint.Attributes.StartDate, sprint.Attributes.FinishDate);

			var workItems = (await azureDevOps.GetWorkItems(sprint))
				.OrderBy(i => i.StackRank)
				//.Where(i => new[] { "Bug", "User Story" }.Contains(i.Type))
				.ToList();

			var workItemsProjected = workItems.Select(i => new
			{
				i.Id,
				i.Title,
				i.State,
				Topic = i.ParentTitle,
				i.Tags,
			});

			switch (Output)
			{
				case "json":
					{
						Boring(Json(workItemsProjected, indented: false));
						break;
					}
				case "json+pretty":
					{
						Boring(Json(workItemsProjected, indented: true));
						break;
					}
				default:
					{
						foreach (var workItem in workItems)
						{
							AnsiConsole.Write(Display2(workItem));
							AnsiConsole.WriteLine();
						}
						break;
					}
			}
		}
	}
}
