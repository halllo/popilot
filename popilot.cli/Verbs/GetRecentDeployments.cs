using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi.Types;
using Spectre.Console;
using System.Diagnostics;
using System.Text;

namespace popilot.cli.Verbs
{
	[Verb("get-recent-deployments")]
	class GetRecentDeployments
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }

		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		[Option(longName: "path", Required = false, HelpText = "path of pipelines")]
		public string? PathPrefix { get; set; }

		[Option('h', longName: "hours", Required = false, HelpText = "recent hours (default is 8)")]
		public int RecentHours { get; set; } = 8;

		[Option('d', longName: "document", Required = false, HelpText = "Generates a document.")]
		public bool GenerateDocument { get; set; }

		[Option('r', longName: "releasenotes", Required = false, HelpText = "release notes (Uri)")]
		public Uri? ReleaseNotesUrl { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetPipelines> logger)
		{
			var teamContext = new TeamContext(Project ?? azureDevOps.options.Value.DefaultProject);
			await azureDevOps.Init();
			var definitions = await azureDevOps.buildClient!.GetDefinitionsAsync(teamContext.Project, includeLatestBuilds: true, path: PathPrefix ?? (Team != null ? $"\\{(Team).Replace("Team", "").Trim()}" : null));
			var recentDeployments = definitions
				.Where(d => true)
				.OrderBy(d => d.Name)
				.ToAsyncEnumerable()
				.SelectAwait(async definition =>
				{
					var latestBuild = definition.LatestBuild;
					var timeline = latestBuild?.Id != null ? await azureDevOps.buildClient.GetBuildTimelineAsync(teamContext.Project, latestBuild.Id) : null;
					var lastEvent = timeline?.Records.MaxBy(r => r.FinishTime);
					var stages = timeline?.Records != null ? timeline.Records.Where(r => r.RecordType == "Stage").OrderBy(r => r.Order) : Enumerable.Empty<TimelineRecord>();
					var stageStatus = string.Join("-", stages.Select(GetPipeline.Icon));
					var hasProd = stages.Any(s => s.Name.StartsWith("Production"));
					var successfulOnProd = stages.Any(s => s.Name.StartsWith("Production") && s.State == TimelineRecordState.Completed && s.Result == TaskResult.Succeeded);
					var artifactName = definition.Name.Replace("-main", "");
					return new { artifactName, latestBuild, timeline, lastEvent, stageStatus, hasProd, successfulOnProd };
				})
				.Where(r => r.successfulOnProd && r.lastEvent?.FinishTime > DateTime.Now.AddHours(-1 * RecentHours));


			var html = new StringBuilder();
			html.AppendLine("<html><head><style>table { border-collapse: collapse; } th, td { border: 1px solid black; padding: 8px; } th { background-color: #f2f2f2; }</style></head><body>");
			html.AppendLine("We have deployed the following services to production:");
			html.AppendLine("<ul>");
			await foreach (var deployment in recentDeployments)
			{
				html.AppendLine($"<li>{deployment.artifactName} {deployment.latestBuild?.BuildNumber}</li>");
			}
			html.AppendLine("</ul>");
			if (ReleaseNotesUrl != null)
			{
				html.AppendLine($"\nRelease Notes are available at <a href=\"{ReleaseNotesUrl}\">{ReleaseNotesUrl}</a>.");
			}
			html.AppendLine("</body></html>");


			if (GenerateDocument)
			{
				var fileName = $"recentdeployments_{DateTime.Now:yyyyMMdd-HHmmss}.html";
				File.WriteAllText(fileName, html.ToString());
				Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
			}
			else
			{
				Console.WriteLine(html.ToString());
			}
		}
	}
}