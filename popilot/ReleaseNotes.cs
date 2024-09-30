using Microsoft.TeamFoundation.Work.WebApi;
using System.Text;
using System.Text.RegularExpressions;
using static popilot.AzureDevOps;

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

		public async Task<IReleaseNotesWorkItems> OfRecentClosings(string? project, string? team, int? take = null, CancellationToken cancellationToken = default)
		{
			var filteredIterationReferences = (await azureDevOps.GetAllIterations(project, team, cancellationToken))
				.Where(i => i.Attributes.TimeFrame == TimeFrame.Current || i.Attributes.TimeFrame == TimeFrame.Past)
				.TakeLast(take ?? 10);

			var iterations = await azureDevOps.GetIterationsWithCompletedWorkItems(project, team, filteredIterationReferences, cancellationToken: cancellationToken);

			var closingDays = iterations
				.Reverse()
				.Select(i => i.WorkItems
					.GroupBy(w => (DateOnly?)(w.ClosedDate.HasValue ? DateOnly.FromDateTime(w.ClosedDate.Value) : null))
					.OrderByDescending(d => d.Key)
				)
				.SelectMany(i => i.Select(g => new WorkItemsGroup
				(
					GroupName: $"{g.Key:dd.MM.yyyy}",
					WorkItems: g.Where(w => w.Tags.Contains("_ReleaseNotes") || w.Tags.Contains("ReleaseNotes")).ToList()
				)))
				.Where(i => i.WorkItems.Any())
				.ToList();

			return new GroupedReleaseNotesWorkItems(closingDays);
		}

		public async Task<IReleaseNotesWorkItems> OfRecentDeployments(string? project, string? team, int? take = null, CancellationToken cancellationToken = default)
		{
			//todo: get all builds from all definitions, on production, get their work items, group by date
			throw new NotImplementedException();
		}

		public interface IReleaseNotesWorkItems
		{
			string Html(bool retainFirstH1, bool showTags, string[]? allowedTags = null);
			string Md();
			string Xml();
		}

		public record WorkItemsGroup(string GroupName, IReadOnlyList<IWorkItemDto> WorkItems);

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

			public string Md()
			{
				throw new NotImplementedException();
			}

			public string Xml()
			{
				throw new NotImplementedException();
			}
		}

		public record GroupedReleaseNotesWorkItems(IReadOnlyList<WorkItemsGroup> GroupedWorkItems) : IReleaseNotesWorkItems
		{
			public string Html(bool retainFirstH1, bool showTags, string[]? allowedTags = null)
			{
				var stringBuilder = new StringBuilder();
				stringBuilder.AppendLine("""<style>h3 { margin-top: 0.7rem; }</style>""");
				stringBuilder.AppendLine("""<div style="font-family: sans-serif;">""");
				foreach (var group in GroupedWorkItems)
				{
					stringBuilder.AppendLine($"""<h3>{group.GroupName}</h3>""");
					foreach (var workItem in group.WorkItems)
					{
						stringBuilder.AppendLine($""""
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

			public string Md()
			{
				var stringBuilder = new StringBuilder();
				stringBuilder.AppendLine("# Release Notes");
				stringBuilder.AppendLine();
				foreach (var group in GroupedWorkItems)
				{
					stringBuilder.AppendLine($"## {group.GroupName}");
					stringBuilder.AppendLine();
					foreach (var workItem in group.WorkItems)
					{
						stringBuilder.AppendLine($"#{workItem.Id}");
						stringBuilder.AppendLine();

						var relasenotes = Unhtml.Clean(workItem.ReleaseNotes);
						stringBuilder.AppendLine($"""{relasenotes}""");
						stringBuilder.AppendLine();
					}
				}
				var releaseNotes = stringBuilder.ToString();
				return releaseNotes;
			}

			public string Xml()
			{
				var stringBuilder = new StringBuilder();
				stringBuilder.AppendLine("<ReleaseNotes>");
				foreach (var group in GroupedWorkItems)
				{
					stringBuilder.AppendLine($"<Group Name=\"{group.GroupName}\">");
					foreach (var workItem in group.WorkItems)
					{
						stringBuilder.AppendLine($"<WorkItem Id=\"{workItem.Id}\">");
						var relasenotes = Unhtml.Clean(workItem.ReleaseNotes);
						stringBuilder.AppendLine($"""{relasenotes}""");
						stringBuilder.AppendLine("</WorkItem>");
					}
					stringBuilder.AppendLine($"</Group>");
				}
				stringBuilder.AppendLine("</ReleaseNotes>");
				var releaseNotes = stringBuilder.ToString();
				return releaseNotes;
			}
		}
	}
}
