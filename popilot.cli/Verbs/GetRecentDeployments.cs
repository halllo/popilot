using CommandLine;
using Microsoft.Extensions.Logging;
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

		[Option(longName: "latestBuilds", Required = false, HelpText = "how many builds to look back (default is 1)")]
		public int? LatestBuilds { get; set; }

		[Option('h', longName: "hours", Required = false, HelpText = "recent hours (default is 8)")]
		public int RecentHours { get; set; } = 8;

		[Option('d', longName: "document", Required = false, HelpText = "Generates a document.")]
		public bool GenerateDocument { get; set; }

		[Option('r', longName: "releasenotes", Required = false, HelpText = "release notes (Uri)")]
		public Uri? ReleaseNotesUrl { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetPipelines> logger)
		{
			var recentDeployedBuilds = azureDevOps.GetDeployableBuilds(Project, Team, PathPrefix, LatestBuilds)
				.Where(r => r.successfulOnProd && r.lastEvent?.FinishTime > DateTime.Now.AddHours(-1 * RecentHours));

			var recentDeployedReleases = azureDevOps.GetDeployableReleases(Project, null, null)
				.Where(r => r.OnProduction && r.LastModifiedOn > DateTime.Now.AddHours(-1 * RecentHours));

			var recentlyDeployed = AsyncEnumerable.Concat(
				recentDeployedBuilds.Select(r => new { name = r.artifactName.Replace("-main", "", StringComparison.InvariantCultureIgnoreCase), version = r.latestBuild.BuildNumber }),
				recentDeployedReleases.Select(r => new { name = r.ArtifactName.Replace("-main", "", StringComparison.InvariantCultureIgnoreCase), version = r.ArtifactVersion }));

			var html = new StringBuilder();
			html.AppendLine("<html><head><style>table { border-collapse: collapse; } th, td { border: 1px solid black; padding: 8px; } th { background-color: #f2f2f2; }</style></head><body>");
			html.AppendLine("We have deployed the following services to production:");
			html.AppendLine("<ul>");
			await foreach (var deployment in recentlyDeployed.OrderBy(d => d.name))
			{
				html.AppendLine($"<li>{deployment.name} {deployment.version}</li>");
			}
			html.AppendLine("</ul>");
			if (ReleaseNotesUrl != null)
			{
				html.AppendLine($"\nRelease Notes are available <a href=\"{ReleaseNotesUrl}\">here</a>.");
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