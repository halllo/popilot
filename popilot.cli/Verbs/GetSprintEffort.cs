using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using static ColoredConsole;

namespace popilot.cli.Verbs
{
	[Verb("get-sprint-effort")]
	class GetSprintEffort
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }
		[Option(longName: "display-workitems", Required = false)]
		public bool DisplayWorkItems { get; set; }

		[Value(0, MetaName = "sprint path", Required = false)]
		public string? Path { get; set; }
		[Option(longName: "skip-sprints", Required = false)]
		public int SkipSprints { get; set; }

		[Option('p', longName: "group-by-tags", HelpText = "Comma seperated list of tags to group by.", Required = false)]
		public string? GoupByTags { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetSprintEffort> logger)
		{
			List<AzureDevOps.IWorkItemDto> workItems = [];
			if (!string.IsNullOrWhiteSpace(Path))
			{
				var sprints = await azureDevOps.GetIterationsUnder(Path, Project, Team);
				if (!sprints.Any())
				{
					logger.LogError("Sprint {Path} not found.", Path);
					return;
				}

				foreach (var sprint in sprints.Skip(SkipSprints))
				{
					logger.LogInformation("Sprint {Name} from {Start:dd.MM.yyyy} to {Finish:dd.MM.yyyy}", sprint.Name, sprint.Attributes.StartDate, sprint.Attributes.FinishDate);
					workItems.AddRange(await GetEffort(azureDevOps, sprint));
					Console.WriteLine();
				}
			}
			else
			{
				var sprints = await azureDevOps.GetIterations(Project, Team);
				foreach (var sprint in sprints
					.Where(s => s.Attributes.TimeFrame != Microsoft.TeamFoundation.Work.WebApi.TimeFrame.Past)
					.Skip(SkipSprints))
				{
					logger.LogInformation("Sprint {Name} from {Start:dd.MM.yyyy} to {Finish:dd.MM.yyyy}", sprint.Name, sprint.Attributes.StartDate, sprint.Attributes.FinishDate);
					workItems.AddRange(await GetEffort(azureDevOps, sprint));
					Console.WriteLine();
				}
			}

			AnsiConsole.MarkupLine("[bold]Total Effort[/]");
			GetEffort(azureDevOps, workItems);
		}

		private async Task<IReadOnlyCollection<AzureDevOps.IWorkItemDto>> GetEffort(AzureDevOps azureDevOps, Microsoft.TeamFoundation.Work.WebApi.TeamSettingsIteration sprint)
		{
			var workItems = await azureDevOps.GetWorkItems(sprint);
			GetEffort(azureDevOps, workItems);
			return workItems;
		}

		private void GetEffort(AzureDevOps azureDevOps, IReadOnlyCollection<AzureDevOps.IWorkItemDto> workItems)
		{
			var groups = GoupByTags?.Split(",", StringSplitOptions.RemoveEmptyEntries);
			var workItemGroups = workItems
				.OrderBy(wi => wi.StackRank)
				.Select(wi => new
				{
					workitem = wi,
					tags = wi.Tags.Concat(wi.Parents.SelectMany(p => p.Tags)).Distinct().ToArray(),
				})
				.Select(wi => new
				{
					wi.workitem,
					wi.tags,
					group = (groups ?? []).Intersect(wi.tags).FirstOrDefault() ?? null,
				})
				.GroupBy(wi => wi.group, wi => wi.workitem)
				.OrderBy(wig => wig.Key)
				.ToList();

			var allWorkItems = workItemGroups.SelectMany(g => g);
			var allOriginalEstimate = allWorkItems.Sum(wi => wi.OriginalEstimate ?? 0);
			var allRemainingWork = allWorkItems.Sum(wi => wi.RemainingWork ?? 0);
			var allCompletedWork = allWorkItems.Sum(wi => wi.CompletedWork ?? 0);
			foreach (var wig in workItemGroups)
			{
				var originalEstimate = wig.Sum(wi => wi.OriginalEstimate ?? 0);
				var remainingWork = wig.Sum(wi => wi.RemainingWork ?? 0);
				var completedWork = wig.Sum(wi => wi.CompletedWork ?? 0);
				AnsiConsole.MarkupLine($"{wig.Key ?? "<no group>"} [gray]remaining[/] {remainingWork}h [gray](estimated[/] {originalEstimate}h [magenta]{originalEstimate / allOriginalEstimate:P0}[/][gray])[/]");
				if (DisplayWorkItems)
				{
					foreach (var wi in wig)
					{
						AnsiConsole.Write(Display2WithRemainingWork(wi));
						AnsiConsole.WriteLine();
					}
					AnsiConsole.WriteLine();
				}
			}
		}
	}
}
