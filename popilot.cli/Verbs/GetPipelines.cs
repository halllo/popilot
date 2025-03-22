using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi.Types;
using Spectre.Console;
using static ColoredConsole;

namespace popilot.cli.Verbs
{
	[Verb("get-pipelines")]
	class GetPipelines
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }

		[Option(longName: "path", Required = false, HelpText = "path of pipelines")]
		public string? PathPrefix { get; set; }

		[Option('s', longName: "search", Required = false, HelpText = "search string")]
		public string? SearchText { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetPipelines> logger)
		{
			var deployableBuilds = azureDevOps.GetDeployableBuilds(Project, null, PathPrefix)
				.Where(d => SearchText == null || d.artifactName.Contains(SearchText, StringComparison.InvariantCultureIgnoreCase))
				.Select(d =>
				{
					var stageStatus = string.Join("-", d.stages.Select(GetPipeline.Icon));
					return new { d.definition, d.latestBuild, d.timeline, d.lastStageEvent, stageStatus, d.hasProd, d.successfulOnProd };
				})
				.OrderByDescending(r => r.lastStageEvent?.FinishTime);

			var table = new Table().Expand();
			table.AddColumn("#");
			table.AddColumn("Pipeline");
			table.AddColumn("Latest Build");
			table.AddColumn("Status");
			table.AddColumn("Finish Time");
			table.ShowRowSeparators = true;
			var rows = await deployableBuilds.ToListAsync();
			foreach (var row in rows)
			{
				table.AddRow(
					new Markup($"[link={row.definition.UrlHumanReadable()}][gray]{row.definition.Id}[/][/]"),
					new Markup($"{row.definition.Name}"),
					new Markup($"[gray]{row.latestBuild?.BuildNumber}[/]"),
					new Markup(row.stageStatus),
					new Markup($"[gray]{row.lastStageEvent?.FinishTime}[/]")
				);
			}
			AnsiConsole.Write(table);

			{
				var countProd = rows.Count(r => r.hasProd);
				var countOnProd = rows.Count(r => r.successfulOnProd);
				var runningOnProd = countOnProd / (double)countProd;
				logger.LogInformation($"{{RunningOnProd:P}} running on production ({countOnProd}/{countProd}).", runningOnProd);
			}
		}
	}

	[Verb("get-pipeline")]
	class GetPipeline
	{
		[Value(0, MetaName = "definition ID (int)", Required = true)]
		public int DefinitionId { get; set; }

		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetPipeline> logger)
		{
			var teamContext = new TeamContext(Project ?? azureDevOps.options.Value.DefaultProject, azureDevOps.options.Value.DefaultTeam);
			await azureDevOps.Init();
			var definition = await azureDevOps.buildClient!.GetDefinitionAsync(teamContext.Project, DefinitionId);
			Boring(Json(definition));

			var builds = await azureDevOps.buildClient.GetBuildsAsync(teamContext.Project, new[] { definition.Id });

			var table = new Table().Expand();
			table.AddColumn("#");
			table.AddColumn("Build");
			table.AddColumn("");
			table.AddColumn("LastStage");
			table.ShowRowSeparators = true;

			foreach (var build in builds.OrderByDescending(b => b.QueueTime))
			{
				var timeline = await azureDevOps.buildClient.GetBuildTimelineAsync(teamContext.Project, build.Id);
				var lastStage = timeline.Records.Where(r => r.RecordType == "Stage").MaxBy(r => r.FinishTime);
				var stages = timeline.Records.Where(r => r.RecordType == "Stage").OrderBy(r => r.Order);

				var stageStatus = string.Join("-", stages.Select(Icon));

				table.AddRow(
					new Markup($"[gray]{build.Id}[/]"),
					new Markup($"{build.BuildNumber}"),
					new Markup(stageStatus),
					new Markup($"[gray]{lastStage?.Name} {lastStage?.FinishTime}[/]")
					);
			}
			AnsiConsole.Write(table);
			logger.LogInformation("{Count} builds loaded", builds.Count);
		}

		public static string Icon(TimelineRecord record)
		{
			return record switch
			{
				{ State: TimelineRecordState.Pending } => Emoji.Known.FourOClock,
				{ State: TimelineRecordState.InProgress } => Emoji.Known.BlueSquare,
				{ State: TimelineRecordState.Completed, Result: TaskResult.Succeeded } => Emoji.Known.GreenSquare,
				{ State: TimelineRecordState.Completed, Result: TaskResult.Skipped } => Emoji.Known.WhiteLargeSquare,
				{ State: TimelineRecordState.Completed, Result: TaskResult.Failed } => Emoji.Known.RedSquare,
				_ => "??",
			};
		}
	}

	[Verb("get-build")]
	class GetBuild
	{
		[Value(0, MetaName = "build ID (int)", Required = true)]
		public int BuildId { get; set; }

		[Option('a', longName: "all", Required = false, HelpText = "show all records, not just stages")]
		public bool ShowAllRecords { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetBuild> logger)
		{
			var teamContext = new TeamContext(azureDevOps.options.Value.DefaultProject, azureDevOps.options.Value.DefaultTeam);
			await azureDevOps.Init();

			var timeline = await azureDevOps.buildClient!.GetBuildTimelineAsync(teamContext.Project, BuildId);

			var table = new Table().Expand();
			table.AddColumn("#");
			table.AddColumn("Name");
			table.AddColumn("FinishTime");
			table.AddColumn("Status");
			table.ShowRowSeparators = true;

			var filteredRecords = timeline.Records
				.Where(r => ShowAllRecords || r.RecordType == "Stage")
				.OrderBy(r => r.Order)
				.ToList();

			foreach (var record in filteredRecords)
			{
				table.AddRow(
					new Markup($"[gray]{record.RecordType}[/]"),
					new Markup($"{record.Name}"),
					new Markup($"{record.FinishTime}"),
					new Markup(record switch
					{
						{ State: TimelineRecordState.Completed, Result: TaskResult.Succeeded } => $"[green]Succeeded[/]",
						{ State: TimelineRecordState.Completed, Result: TaskResult.Failed } => $"[red]Failed[/]",
						_ => $"{record.State} {record.Result}".Trim()
					}));
			}
			AnsiConsole.Write(table);
			logger.LogInformation("{Count} records loaded", filteredRecords.Count);

			var workItems = await azureDevOps.GetBuildRelatedWorkItems(teamContext.Project, BuildId);

			Console.WriteLine();
			foreach (var workItem in workItems)
			{
				Display(workItem);
			}
			logger.LogInformation("{Count} work items referenced", workItems.Count);
		}
	}


	[Verb("get-releasepipelines")]
	class GetReleases
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }

		[Option(longName: "path", Required = false, HelpText = "path of pipelines")]
		public string? PathPrefix { get; set; }

		[Option('s', longName: "search", Required = false, HelpText = "search string")]
		public string? SearchText { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetPipelines> logger)
		{
			var releases = await azureDevOps.GetDeployableReleases(Project, SearchText).ToListAsync();
			Json.Out(releases);
		}
	}
}
