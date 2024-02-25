using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace popilot.Verbs
{
	[Verb("get-releasenotes")]
	class GetReleaseNotes
	{
		[Value(0, MetaName = "work item ID (int)", Required = false)]
		public int? Id { get; set; }

		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		[Option(longName: "hide-first-h1", Required = false, HelpText = "hide first H1")]
		public bool HideFirstH1 { get; set; }

		[Option('d', longName: "document", Required = false, HelpText = "Generates a document.")]
		public bool GenerateDocument { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetReleaseNotes> logger)
		{
			var releaseNotesReader = new ReleaseNotes(azureDevOps);
			string html;

			if (Id.HasValue)
			{
				var releaseNotes = await releaseNotesReader.OfWorkItem(Id.Value);
				html = releaseNotes.Html(retainFirstH1: !HideFirstH1);

				if (GenerateDocument)
				{
					var fileName = $"releasenotes_{Id}_{DateTime.Now:yyyyMMdd-HHmmss}.html";
					File.WriteAllText(fileName, html);
					Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
				}
				else
				{
					Console.WriteLine(html);
				}
			}
			else
			{
				var releaseNotes = await releaseNotesReader.OfLastSprints(Project, Team);
				releaseNotes.ConsoleOut();
			}
		}
	}

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
			void ConsoleOut();
		}

		private record HierarchicalReleaseNotesWorkItems(AzureDevOps.IWorkItemDto Root, IReadOnlyList<AzureDevOps.IWorkItemDto> WorkItems) : IReleaseNotesWorkItems
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

			public void ConsoleOut()
			{
				throw new NotImplementedException();
			}
		}

		private record FlatReleaseNotesWorkItems(IReadOnlyList<AzureDevOps.IterationWithWorkItems> IterationsWithWorkItems) : IReleaseNotesWorkItems
		{
			public string Html(bool retainFirstH1)
			{
				throw new NotImplementedException();
			}

			public void ConsoleOut()
			{
				foreach (var iteration in IterationsWithWorkItems.Reverse())
				{
					AnsiConsole.MarkupLine($"[bold]{iteration.Iteration.Attributes.FinishDate:dd.MM.yyyy}[/] {iteration.Iteration.Path}");
					foreach (var workItem in iteration.WorkItems)
					{
						var panel = new Panel(workItem.ReleaseNotes);
						panel.Header = new PanelHeader($"#{workItem.Id}") { Justification = Justify.Left };
						panel.Expand = true;
						panel.Border = BoxBorder.Rounded;
						panel.BorderStyle = new Style(foreground: Color.Grey);
						AnsiConsole.Write(panel);
					}
					Console.WriteLine();
				}
			}
		}
	}
}
