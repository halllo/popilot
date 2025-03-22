using CommandLine;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

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
		public int LatestBuilds { get; set; } = 1;

		[Option('h', longName: "hours", Required = false, HelpText = "recent hours (default is 8)")]
		public int RecentHours { get; set; } = 8;

		[Option(longName: "onlyLastSuccessfulBuild", Required = false, HelpText = "ignore older than last successful build")]
		public bool OnlyLastSuccessfulBuild { get; set; }

		[Option('d', longName: "document", Required = false, HelpText = "Generates a document.")]
		public bool GenerateDocument { get; set; }

		[Option('r', longName: "releasenotes", Required = false, HelpText = "release notes (Uri)")]
		public Uri? ReleaseNotesUrl { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetPipelines> logger)
		{
			var releaseNotification = new ReleaseNotification(azureDevOps);

			var html = await releaseNotification.Html(Project, Team, PathPrefix, LatestBuilds, RecentHours, OnlyLastSuccessfulBuild, ReleaseNotesUrl);

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