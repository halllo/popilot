using System.Text;

namespace popilot
{
	public class ReleaseNotification
	{
		private readonly AzureDevOps azureDevOps;

		public ReleaseNotification(AzureDevOps azureDevOps)
		{
			this.azureDevOps = azureDevOps;
		}

		public async Task<string> Html(string? project, string? team, string? pathPrefix = null, int latestBuilds = 1, double recentHours = 8, bool onlyLastSuccessfulBuild = false, Uri? releaseNotesUrl = null, CancellationToken cancellationToken = default)
		{
			var recentDeployedBuilds = azureDevOps.GetDeployableBuilds(project, team, pathPrefix, latestBuilds)
				.Where(r => r.successfulOnProd && r.lastStageEvent?.FinishTime > DateTime.Now.AddHours(-1 * recentHours));

			var recentDeployedReleases = azureDevOps.GetDeployableReleases(project, null, null)
				.Where(r => r.OnProduction && r.LastModifiedOn > DateTime.Now.AddHours(-1 * recentHours));

			var recentlyDeployed = AsyncEnumerable.Concat(
				recentDeployedBuilds.Select(r => new { name = r.artifactName.Replace("-main", "", StringComparison.InvariantCultureIgnoreCase), version = r.latestBuild.BuildNumber }),
				recentDeployedReleases.Select(r => new { name = r.ArtifactName.Replace("-main", "", StringComparison.InvariantCultureIgnoreCase), version = r.ArtifactVersion })
			);

			if (onlyLastSuccessfulBuild)
			{
				recentlyDeployed = recentlyDeployed
					.GroupBy(d => d.name)
					.SelectAwait(d => d.OrderByDescending(o => o.version).FirstAsync());
			}

			var html = new StringBuilder();
			html.AppendLine("<html><head><style>table { border-collapse: collapse; } th, td { border: 1px solid black; padding: 8px; } th { background-color: #f2f2f2; }</style></head><body>");
			html.AppendLine("We have deployed the following services to production:");
			html.AppendLine("<ul>");
			await foreach (var deployment in recentlyDeployed.OrderBy(d => d.name))
			{
				html.AppendLine($"<li>{deployment.name} {deployment.version}</li>");
			}
			html.AppendLine("</ul>");
			if (releaseNotesUrl != null)
			{
				html.AppendLine($"\nRelease Notes are available <a href=\"{releaseNotesUrl}\">here</a>.");
			}
			html.AppendLine("</body></html>");
			return html.ToString();
		}
	}
}
