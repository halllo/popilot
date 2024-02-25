using System.Text.RegularExpressions;

namespace popilot
{
	public class ReleaseNotes
	{
		private readonly AzureDevOps azureDevOps;

		public ReleaseNotes(AzureDevOps azureDevOps)
		{
			this.azureDevOps = azureDevOps;
		}

		public async Task<IReleaseNotesWorkItems> OfWorkItem(int workItemId, CancellationToken cancellationToken = default)
		{
			var release = await azureDevOps.GetWorkItemWithChildren(workItemId, cancellationToken);
			var root = release.Vertices.Single(v => v.Id == workItemId);
			var releaseNotess = release
				.Dfs(root, edges: outEdges => outEdges.OrderBy(e => e.Target.StackRank).ThenBy(e => e.Target.Title/*or ID?*/))
				.Where(wi => wi.Tags.Contains("_ReleaseNotes"))
				.ToList();

			return new HierarchicalReleaseNotesWorkItems(root, releaseNotess);
		}

		public async Task<IReleaseNotesWorkItems> OfLastSprints(string? project, string? team, CancellationToken cancellationToken = default)
		{
			var iterations = await azureDevOps.GetPastIterationsWithCompletedWorkItems(project, team, cancellationToken: cancellationToken);
			return new FlatReleaseNotesWorkItems(iterations
				.Select(i => new AzureDevOps.IterationWithWorkItems(
					i.Iteration,
					i.WorkItems.Where(w => w.Tags.Contains("_ReleaseNotes")).ToList()
				))
				.Where(i => i.WorkItems.Any())
				.ToList()
			);
		}

		public interface IReleaseNotesWorkItems
		{
			string Html(bool retainFirstH1);
		}

		public record HierarchicalReleaseNotesWorkItems(AzureDevOps.IWorkItemDto Root, IReadOnlyList<AzureDevOps.IWorkItemDto> WorkItems) : IReleaseNotesWorkItems
		{
			static readonly Regex matchFirstH1 = new("(?<!(^.*?<h1.*?))<h1.*?<\\/h1>", RegexOptions.Compiled);

			public string Html(bool retainFirstH1)
			{
				var intro = string.Join("\n\n", WorkItems.Intersect(new[] { Root }).Select(wi => wi.ReleaseNotes))
					.Return()
					.Select(i => retainFirstH1 ? i : matchFirstH1.Replace(i, string.Empty))
					.Single();

				var releaseNotes = string.Join("<br>\n\n", WorkItems.Except(new[] { Root }).Select(wi => $"""<span style="color: gray;">#{wi.Id}</span> {wi.ReleaseNotes}"""));

				var page = intro.Contains("{{children}}")
					? $"""
							<div style="font-family: sans-serif;">
								{intro.Replace("{{children}}", releaseNotes)}
							</div>
							"""
					: $"""
							<div style="font-family: sans-serif;">
								{intro}
								<br>
								<hr>
								<br>
								{(releaseNotes.Any() ? $"<h2><a id=\"releasenotes\">Release Notes</a></h2>\n{releaseNotes}" : "")}
							</div>
							""";
				return page;
			}
		}

		public record FlatReleaseNotesWorkItems(IReadOnlyList<AzureDevOps.IterationWithWorkItems> IterationsWithWorkItems) : IReleaseNotesWorkItems
		{
			public string Html(bool retainFirstH1)
			{
				throw new NotImplementedException();
			}
		}
	}
}
