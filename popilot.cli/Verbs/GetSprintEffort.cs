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
		[Option('o', longName: "output", Required = false)]
		public string? Output { get; set; }

		[Value(0, MetaName = "sprint path", Required = false)]
		public string? Path { get; set; }

		[Option('p', longName: "group-by-tags", HelpText = "Comma seperated list of tags to group by.", Required = false)]
		public string? GoupByTags { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetSprintEffort> logger)
		{
			if (!string.IsNullOrWhiteSpace(Path))
			{
				var sprint = await azureDevOps.GetIteration(Path, Project, Team);
				if (sprint == null)
				{
					logger.LogError("Sprint {Path} not found.", Path);
					return;
				}

				await GetEffort(azureDevOps, logger, sprint);
			}
			else
			{
				var sprints = await azureDevOps.GetIterations(Project, Team);
				foreach (var sprint in sprints.Where(s => s.Attributes.TimeFrame != Microsoft.TeamFoundation.Work.WebApi.TimeFrame.Past))
				{
					await GetEffort(azureDevOps, logger, sprint);
				}
			}
		}

		private async Task GetEffort(AzureDevOps azureDevOps, ILogger<GetSprintEffort> logger, Microsoft.TeamFoundation.Work.WebApi.TeamSettingsIteration sprint)
		{
			logger.LogInformation("Sprint {Name} from {Start:dd.MM.yyyy} to {Finish:dd.MM.yyyy}", sprint.Name, sprint.Attributes.StartDate, sprint.Attributes.FinishDate);

			var groups = GoupByTags?.Split(",", StringSplitOptions.RemoveEmptyEntries);
			var workItemGroups = (await azureDevOps.GetWorkItems(sprint))
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

			foreach (var wig in workItemGroups)
			{
				var originalEstimate = wig.Sum(wi => wi.OriginalEstimate ?? 0);
				var remainingWork = wig.Sum(wi => wi.RemainingWork ?? 0);
				var completedWork = wig.Sum(wi => wi.CompletedWork ?? 0);
				AnsiConsole.MarkupLine($"[bold]{wig.Key ?? "<no group>"} remaining {remainingWork}h (estimated {originalEstimate}h)[/]");
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
