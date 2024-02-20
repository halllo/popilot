using CommandLine;
using Microsoft.Extensions.Logging;
using QuickGraph;
using QuickGraph.Algorithms.Observers;
using QuickGraph.Algorithms.Search;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace popilot.Verbs
{
	[Verb("get-releasenotes")]
	class GetReleaseNotes
	{
		[Value(0, MetaName = "work item ID (int)", Required = true)]
		public int Id { get; set; }

		[Option(longName: "hide-first-h1", Required = false, HelpText = "hide first H1")]
		public bool HideFirstH1 { get; set; }

		[Option('d', longName: "document", Required = false, HelpText = "Generates a document.")]
		public bool GenerateDocument { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetReleaseNotes> logger)
		{
			var releaseNotes = await ReleaseNotes(azureDevOps, Id, firstH1: !HideFirstH1, cancellationToken: default);

			if (GenerateDocument)
			{
				string fileName = $"releasenotes_{Id}_{DateTime.Now:yyyyMMdd-HHmmss}.html";
				File.WriteAllText(fileName, releaseNotes);
				Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
			}
			else
			{
				Console.WriteLine(releaseNotes);
			}
		}

		static readonly Regex matchFirstH1 = new("(?<!(^.*?<h1.*?))<h1.*?<\\/h1>", RegexOptions.Compiled);

		private async Task<string?> ReleaseNotes(AzureDevOps azureDevOps, int releaseId, bool firstH1, CancellationToken cancellationToken)
		{
			var release = await azureDevOps.GetWorkItemWithChildren(releaseId, cancellationToken);
			var root = release.Vertices.Single(v => v.Id == releaseId);

			var dfs = new DepthFirstSearchAlgorithm<AzureDevOps.IWorkItemDto, IEdge<AzureDevOps.IWorkItemDto>>(
				host: null,
				visitedGraph: release,
				colors: new Dictionary<AzureDevOps.IWorkItemDto, GraphColor>(),
				outEdgeEnumerator: outEdges => outEdges.OrderBy(e => e.Target.StackRank).ThenBy(e => e.Target.Title/*or ID?*/));
			var vertexRecorder = new VertexRecorderObserver<AzureDevOps.IWorkItemDto, IEdge<AzureDevOps.IWorkItemDto>>();
			using (vertexRecorder.Attach(dfs))
			{
				dfs.Compute(root);
			}

			var releaseNotess = vertexRecorder.Vertices.Where(wi => wi.Tags.Contains("_ReleaseNotes")).ToList();
			var intro = string.Join("\n\n", releaseNotess.Intersect(new[] { root }).Select(wi => wi.ReleaseNotes))
				.Return()
				.Select(i => firstH1 ? i : matchFirstH1.Replace(i, string.Empty))
				.Single();

			var releaseNotes = string.Join("<br>\n\n", releaseNotess.Except(new[] { root }).Select(wi => $"""<span style="color: gray;">#{wi.Id}</span> {wi.ReleaseNotes}"""));

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
}
