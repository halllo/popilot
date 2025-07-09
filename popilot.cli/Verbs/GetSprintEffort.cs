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
			double effortCompleted(string area) => sprintEfforts.SelectMany(s => s.EffortGroups.Groups).Where(g => g.Area == area).Sum(g => g.Completed);
			double effortEstimated(string area) => sprintEfforts.SelectMany(s => s.EffortGroups.Groups).Where(g => g.Area == area).Sum(g => g.Estimated);
			var areas = sprintEfforts.SelectMany(s => s.EffortGroups.Groups).GroupBy(g => g.Area).ToList();
			foreach (var area in areas)
			{
				var completedPercent = effortCompleted(area.Key) / all;
				var estimatedPercent = effortEstimated(area.Key) / all;
				string percentColor = completedPercent > estimatedPercent ? "red" : completedPercent < estimatedPercent ? "green" : "white";
				table.AddColumn($"[bold]{area.Key}[/]\n{effortCompleted(area.Key)}h[gray]/[/]{all}h [gray]=[/] [{percentColor}]{completedPercent:P0}[/] [gray](est {estimatedPercent:P0})[/]");
			}
			foreach (var effortGroup in sprintEfforts.SelectMany(s => s.EffortGroups.Groups).GroupBy(e => e.Name))
			{
				var rowCompleted = effortGroup.Sum(g => g.Completed);
				var rowCompletedPercent = rowCompleted / all;
				var rowEstimated = effortGroup.Sum(g => g.Estimated);
				var rowEstimatedPercent = rowEstimated / all;
				string rowPercentColor = rowCompletedPercent > rowEstimatedPercent ? "red" : rowCompletedPercent < rowEstimatedPercent ? "green" : "white";
				var rowHeader = $"[bold]{effortGroup.Key}[/]\n{rowCompleted}h[gray]/[/]{all}h [gray]=[/] [{rowPercentColor}]{rowCompletedPercent:P0}[/] [gray](est {rowEstimatedPercent:P0})[/]";
				table.AddRow([
					new Markup(rowHeader),
					..areas.Select(area => 
					{
						var completed = effortGroup.Where(g => g.Area == area.Key).Sum(g => g.Completed);
						var completedPercent = completed / all;
						var estimated = effortGroup.Where(g => g.Area == area.Key).Sum(g => g.Estimated);
						var estimatedPercent = estimated / all;
						string percentColor = completedPercent > estimatedPercent ? "red" : completedPercent < estimatedPercent ? "green" : "white";
						var cell = $"{completed}h[gray]/[/]{all}h [gray]=[/] [{percentColor}]{completedPercent:P0}[/] [gray](est {estimatedPercent:P0})[/]";
						return new Markup(cell);
					})
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
				? $"[gray]comp[/] {allCompletedWork()}h [gray](est {allOriginalEstimate()}h)[/]"
				: $"[gray]rem[/] {allRemainingWork()}h[gray], est[/] {allOriginalEstimate()}h");

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
				double columnCompPercent = allCompletedWork(area) / allCompletedWork();
				double columnOrgPercent = allOriginalEstimate(area) / allOriginalEstimate();
				string columnPercentColor = columnCompPercent > columnOrgPercent ? "red" : columnCompPercent < columnOrgPercent ? "green" : "white";
				var columnHeaderMarkup = past
					? $"[bold]{area}[/]\n[gray]comp[/] {allCompletedWork(area)}h[gray],[/] [{columnPercentColor}]{columnCompPercent:P0}[/] [gray](est {columnOrgPercent:P0})[/]"
					: $"[bold]{area}[/]\n[gray]rem[/] {allRemainingWork(area)}h[gray], est[/] {allOriginalEstimate(area)}h[gray],[/] [magenta]{columnOrgPercent:P0}[/]";
				table.AddColumn(columnHeaderMarkup);
			}

			var effortGroups = new List<EffortGroup>();
			foreach (var wig in workItemGroups)
			{
				double originalEstimate(string? area = null) => wig.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.OriginalEstimate ?? 0);
				double remainingWork(string? area = null) => wig.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.RemainingWork ?? 0);
				double completedWork(string? area = null) => wig.Where(w => area == null || w.AreaPath == area).Sum(wi => wi.CompletedWork ?? 0);

				foreach (var area in areas)
				{
					effortGroups.Add(new EffortGroup(wig.Key ?? "<no group>", area, 
						Estimated: originalEstimate(area), 
						Completed: past ? completedWork(area) : originalEstimate(area)));
				}

				double rowCompPercent = completedWork() / allCompletedWork();
				double rowOrgPercent = originalEstimate() / allOriginalEstimate();
				string rowPercentColor = rowCompPercent > rowOrgPercent ? "red" : rowCompPercent < rowOrgPercent ? "green" : "white";
				var rowHeaderMarkup = past
					? $"[bold]{wig.Key ?? "<no group>"}[/]\n[gray]comp[/] {completedWork()}h[gray],[/] [{rowPercentColor}]{rowCompPercent:P0}[/] [gray](est {rowOrgPercent:P0})[/]"
					: $"[bold]{wig.Key ?? "<no group>"}[/]\n[gray]rem[/] {remainingWork()}h[gray], est[/] {originalEstimate()}h[gray],[/] [magenta]{rowOrgPercent:P0}[/]";

				string cellMarkup(string area)
				{
					double comp = completedWork(area);
					double compAll = allCompletedWork();
					double rem = remainingWork(area);
					double org = originalEstimate(area);
					double orgAll = allOriginalEstimate();
					double compPercent = comp / compAll;
					double orgPercent = org / orgAll;
					string percentColor = compPercent > orgPercent ? "red" : compPercent < orgPercent ? "green" : "white";
					return past
						? $"[gray]comp[/] {comp}h[gray],[/] [{percentColor}]{compPercent:P0}[/] [gray](est {orgPercent:P0})[/]"
						: $"[gray]rem[/] {rem}h[gray], est[/] {org}h[gray],[/] [magenta]{orgPercent:P0}[/]";
				}

				table.AddRow([
					new Markup(rowHeaderMarkup),
					..areas.Select(area => new Markup(cellMarkup(area)))
				]);

				if (DisplayWorkItems)
				{
					AnsiConsole.MarkupLine(rowHeaderMarkup);
					foreach (var wi in wig)
					{
						AnsiConsole.Write(Display2WithRemainingWork(wi));
						AnsiConsole.WriteLine();
					}
					AnsiConsole.WriteLine();
				}
			}
			AnsiConsole.Write(table);

			return new EffortGroups(past ? allCompletedWork() : allOriginalEstimate(), effortGroups);
		}

		record EffortGroups(double All, List<EffortGroup> Groups);
		record EffortGroup(string Name, string Area, double Estimated, double Completed);
	}
}
