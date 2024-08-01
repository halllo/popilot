using Microsoft.TeamFoundation.Work.WebApi;
using System.Text;
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
				.Where(wi => wi.Tags.Contains("_ReleaseNotes") || wi.Tags.Contains("ReleaseNotes"))
				.ToList();

			return new HierarchicalReleaseNotesWorkItems(root, releaseNotess);
		}

		public async Task<IReleaseNotesWorkItems> OfCurrentOrLastSprints(string? project, string? team, int? take = null, CancellationToken cancellationToken = default)
		{
			var filteredIterationReferences = (await azureDevOps.GetAllIterations(project, team, cancellationToken))
				.Where(i => i.Attributes.TimeFrame == TimeFrame.Current || i.Attributes.TimeFrame == TimeFrame.Past)
				.TakeLast(take ?? 10);

			var iterations = await azureDevOps.GetIterationsWithCompletedWorkItems(project, team, filteredIterationReferences, cancellationToken: cancellationToken);
			return new FlatReleaseNotesWorkItems(iterations
				.Select(i => new AzureDevOps.IterationWithWorkItems(
					i.Iteration,
					i.WorkItems.Where(w => w.Tags.Contains("_ReleaseNotes") || w.Tags.Contains("ReleaseNotes")).ToList()
				))
				.Where(i => i.WorkItems.Any())
				.ToList()
			);
		}

		public interface IReleaseNotesWorkItems
		{
			string Html(bool retainFirstH1, bool showTags, string[]? allowedTags = null);
		}

		public record HierarchicalReleaseNotesWorkItems(AzureDevOps.IWorkItemDto Root, IReadOnlyList<AzureDevOps.IWorkItemDto> WorkItems) : IReleaseNotesWorkItems
		{
			static readonly Regex matchFirstH1 = new("(?<!(^.*?<h1.*?))<h1.*?<\\/h1>", RegexOptions.Compiled);

			public string Html(bool retainFirstH1, bool showTags, string[]? allowedTags = null)
			{
				var intro = string.Join("\n\n", WorkItems.Intersect([Root]).Select(wi => wi.ReleaseNotes))
					.Return()
					.Select(i => retainFirstH1 ? i : matchFirstH1.Replace(i, string.Empty))
					.Single();

				var releaseNotes = string.Join("<br>\n\n", WorkItems.Except([Root]).Select(wi => 
					$"""
						<div class="workitem">
							<span style="color: gray;" class="id">#{wi.Id}</span>
							{(showTags ? $"""<span style="color: gray; font-size: x-small;" class="tags">{string.Join(", ", wi.Tags.Except(["ReleaseNotes"]).Where(t => !t.StartsWith("_")).Where(t => allowedTags == null || allowedTags.Contains(t)))}</span>""" : string.Empty)}
							{wi.ReleaseNotes}
						</div>
					"""));

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
			public string Html(bool retainFirstH1, bool showTags, string[]? allowedTags = null)
			{
				var stringBuilder = new StringBuilder();
				stringBuilder.AppendLine("""<div style="font-family: sans-serif;">""");
				foreach (var iteration in IterationsWithWorkItems.Reverse())
				{
					stringBuilder.AppendLine($"""<h1>{iteration.Iteration.Attributes.FinishDate:dd.MM.yyyy} <span style="color: gray; font-size: small;">{iteration.Iteration.Path.Replace("👍", "").Replace("👎", "")}</span></h1>""");
					foreach (var workItem in iteration.WorkItems)
					{
						stringBuilder.AppendLine(
							$""""
								<div class="workitem">
									<span style="color: gray;" class="id">#{workItem.Id}</span>
									{(showTags ? $"""<span style="color: gray; font-size: x-small;" class="tags">{string.Join(", ", workItem.Tags.Except(["ReleaseNotes"]).Where(r => !r.StartsWith("_")).Where(t => allowedTags == null || allowedTags.Contains(t)))}</span>""" : string.Empty)}
									{workItem.ReleaseNotes}
								</div>
							"""");
						stringBuilder.AppendLine("<br>\n\n");
					}
				}
				stringBuilder.AppendLine("""</div>""");
				var releaseNotes = stringBuilder.ToString();
				return releaseNotes;
			}
		}
	}
}
