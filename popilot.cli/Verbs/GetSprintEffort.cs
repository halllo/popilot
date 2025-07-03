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
		public string? IterationPath { get; set; }
		[Option(longName: "skip-sprints", Required = false)]
		public int SkipSprints { get; set; }
		[Option(longName: "take-sprints", Required = false)]
		public int? TakeSprints { get; set; }

		[Option(longName: "group-by-tags", HelpText = "Comma separated list of tags to group by.", Required = false)]
		public string? GoupByTags { get; set; }

		[Option(longName: "tag-filter", HelpText = "Comma separated list of tags to filter by.", Required = false)]
		public string? TagFilter { get; set; }

		[Option(longName: "ignore-unparented", Required = false)]
		public bool IgnoreUnparented { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetSprintEffort> logger)
		{
			List<SprintEfforts> sprintEfforts = [];
			if (!string.IsNullOrWhiteSpace(IterationPath))
			{
				var sprints = await azureDevOps.GetIterationsUnder(IterationPath, Project, Team);
				if (!sprints.Any())
				{
					logger.LogError("Sprint {Path} not found.", IterationPath);
					return;
				}

				foreach (var sprint in sprints.Skip(SkipSprints).TakeIf(TakeSprints.HasValue, TakeSprints ?? 0))
				{
					AnsiConsole.MarkupLine($"[bold]Sprint {sprint.Name}[/][gray] from {sprint.Attributes.StartDate:dd.MM.yyyy} to {sprint.Attributes.FinishDate:dd.MM.yyyy}[/]");
					sprintEfforts.Add(await GetEffort(azureDevOps, sprint));
					Console.WriteLine();
				}
			}
			else
			{
				var sprints = await azureDevOps.GetIterations(Project, Team);
				foreach (var sprint in sprints
					.Where(s => s.Attributes.TimeFrame != Microsoft.TeamFoundation.Work.WebApi.TimeFrame.Past)
					.Skip(SkipSprints)
					.TakeIf(TakeSprints.HasValue, TakeSprints ?? 0))
				{
					AnsiConsole.MarkupLine($"[bold]Sprint {sprint.Name}[/][gray] from {sprint.Attributes.StartDate:dd.MM.yyyy} to {sprint.Attributes.FinishDate:dd.MM.yyyy}[/]");
					sprintEfforts.Add(await GetEffort(azureDevOps, sprint));
					Console.WriteLine();
				}
			}

			AnsiConsole.MarkupLine($"[bold]Total Effort[/][gray]{(!string.IsNullOrEmpty(TagFilter) ? $" filtered by tag '{TagFilter}'" : string.Empty)}[/]");
			var table = new Table();
			table.ShowRowSeparators();
			table.BorderColor(Color.Grey);
			table.AddColumn($"");
			var all = sprintEfforts.Select(s => s.EffortGroups).Sum(g => g.All);
			double effort(string area) => sprintEfforts.SelectMany(s => s.EffortGroups.Groups).Where(g => g.Area == area).Sum(g => g.Part);
			var areas = sprintEfforts.SelectMany(s => s.EffortGroups.Groups).GroupBy(g => g.Area).ToList();
			foreach (var area in areas)
			{
				table.AddColumn($"[bold]{area.Key}[/]\n{effort(area.Key)}h[gray]/[/]{all}h [gray]=[/] [magenta]{effort(area.Key) / all:P0}[/]");
			}
			foreach (var effortGroup in sprintEfforts.SelectMany(s => s.EffortGroups.Groups).GroupBy(e => e.Name))
			{
				table.AddRow([
					new Markup($"[bold]{effortGroup.Key}[/]\n{effortGroup.Sum(g => g.Part)}h[gray]/[/]{all}h [gray]=[/] [magenta]{effortGroup.Sum(g => g.Part) / all:P0}[/]"),
					..areas.Select(area => new Markup($"{effortGroup.Where(g => g.Area == area.Key).Sum(g => g.Part)}h[gray]/[/]{all}h [gray]=[/] [magenta]{effortGroup.Where(g => g.Area == area.Key).Sum(g => g.Part) / all:P0}[/]"))
				]);
			}
			AnsiConsole.Write(table);
		}

		private async Task<SprintEfforts> GetEffort(AzureDevOps azureDevOps, Microsoft.TeamFoundation.Work.WebApi.TeamSettingsIteration sprint)
		{
			var workItems = await azureDevOps.GetWorkItems(sprint);
			var effortGroups = GetEffort(azureDevOps, workItems, past: sprint.Attributes.TimeFrame == Microsoft.TeamFoundation.Work.WebApi.TimeFrame.Past);
			return new SprintEfforts(sprint, effortGroups);
		}

		record SprintEfforts(Microsoft.TeamFoundation.Work.WebApi.TeamSettingsIteration sprint, EffortGroups EffortGroups);

		private EffortGroups GetEffort(AzureDevOps azureDevOps, IReadOnlyCollection<AzureDevOps.IWorkItemDto> workItems, bool past)
		{
			var groups = GoupByTags?.Split(",", StringSplitOptions.RemoveEmptyEntries);
			var filters = TagFilter?.Split(",", StringSplitOptions.RemoveEmptyEntries) ?? [];
			var workItemGroups = workItems
				.OrderBy(wi => wi.StackRank)
				.WhereIf(IgnoreUnparented, wi => wi.ParentId != null)
				.Select(wi => new
				{
					workitem = wi,
					tags = wi.Tags.Concat(wi.Parents.SelectMany(p => p.Tags)).Distinct().ToArray(),
				})
				.Select(wi => new
				{
					wi.workitem,
					wi.tags,
					group = (groups ?? []).Intersect(wi.tags, InvariantCultureIgnoreCaseComparer.Instance).FirstOrDefault() ?? null,
				})
				.WhereIf(filters.Any(), wi => filters.All(f => f.StartsWith("!")
					? !wi.tags.Contains(f.Substring(1), InvariantCultureIgnoreCaseComparer.Instance)
					: wi.tags.Contains(f, InvariantCultureIgnoreCaseComparer.Instance)
				))
				.GroupBy(wi => wi.group, wi => wi.workitem)
				.OrderBy(wig => wig.Key)
				.ToList();

			var allWorkItems = workItemGroups.SelectMany(g => g);
			double allOriginalEstimate(string? area = null) => allWorkItems.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.OriginalEstimate ?? 0);
			double allRemainingWork(string? area = null) => allWorkItems.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.RemainingWork ?? 0);
			double allCompletedWork(string? area = null) => allWorkItems.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.CompletedWork ?? 0);

			var table = new Table();
			table.ShowRowSeparators();
			table.BorderColor(Color.Grey);
			table.AddColumn(past
				? $"{allCompletedWork()}h"
				: $"{allRemainingWork()}h");

			var areas = workItems
				.GroupBy(workItems => workItems.AreaPath)
				.Select(g => new
				{
					Area = g.Key,
					WorkItems = g.ToList(),
				})
				.OrderBy(g => g.Area)
				.Select(g => g.Area)
				.ToList();

			foreach (var area in areas)
			{
				table.AddColumn(past
					? $"[bold]{area}[/]\n[gray]comp[/] {allCompletedWork(area)}h[gray],[/] [magenta]{allCompletedWork(area) / allCompletedWork():P0}[/]"
					: $"[bold]{area}[/]\n[gray]rem[/] {allRemainingWork(area)}h[gray], est[/] {allOriginalEstimate(area)}h[gray],[/] [magenta]{allOriginalEstimate(area) / allOriginalEstimate():P0}[/]");
			}

			var effortGroups = new List<EffortGroup>();
			foreach (var wig in workItemGroups)
			{
				double originalEstimate(string? area = null) => wig.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.OriginalEstimate ?? 0);
				double remainingWork(string? area = null) => wig.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.RemainingWork ?? 0);
				double completedWork(string? area = null) => wig.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.CompletedWork ?? 0);

				foreach (var area in areas)
				{
					effortGroups.Add(new EffortGroup(wig.Key ?? "<no group>", area, past ? completedWork(area) : remainingWork(area)));
				}

				table.AddRow([
					new Markup(past
						? $"[bold]{wig.Key ?? "<no group>"}[/]\n[gray]comp[/] {completedWork()}h[gray],[/] [magenta]{completedWork() / allCompletedWork():P0}[/]"
						: $"[bold]{wig.Key ?? "<no group>"}[/]\n[gray]rem[/] {remainingWork()}h[gray], est[/] {originalEstimate()}h[gray],[/] [magenta]{originalEstimate() / allOriginalEstimate():P0}[/]"),
					..areas.Select(area => new Markup(past
						? $"[gray]comp[/] {completedWork(area)}h[gray],[/] [magenta]{completedWork(area) / allCompletedWork():P0}[/]"
						: $"[gray]rem[/] {remainingWork(area)}h[gray], est[/] {originalEstimate(area)}h[gray],[/] [magenta]{originalEstimate(area) / allOriginalEstimate():P0}[/]"))
				]);

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
			AnsiConsole.Write(table);

			return new EffortGroups(past ? allCompletedWork() : allRemainingWork(), effortGroups);
		}

		record EffortGroups(double All, List<EffortGroup> Groups);
		record EffortGroup(string Name, string Area, double Part);
	}
}
