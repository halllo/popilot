using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace popilot.cli.Verbs
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

		[Option(longName: "hide-tags", Required = false, HelpText = "hide tags")]
		public bool HideTags { get; set; }

		[Option(longName: "allowed-tags", Required = false, HelpText = "allowed tags")]
		public IEnumerable<string> AllowedTags { get; set; } = [];

		[Option(longName: "replace-by-link", Required = false, HelpText = "replace by link")]
		public string? ReplaceByLink { get; set; }

		[Option('o', longName: "output", Required = false, HelpText = "console (default), html, md, xml")]
		public string? Output { get; set; }

		[Option(longName: "merge-into", Required = false, HelpText = "merge into existing document (only supported for md output)")]
		public string? MergeInto { get; set; }

		[Option(longName: "take", Required = false, HelpText = "take sprints (only used if no Id is provided; default 10)")]
		public int? TakeSprints { get; set; }

		[Option(longName: "iteration-path", Required = false, HelpText = "Iteration path. If not provided, we find it by convention.")]
		public string? IterationPath { get; set; }

		[Option(longName: "iteration-path-filter", Required = false)]
		public Regex? IterationPathFilter { get; set; }

		[Option(longName: "area-path", Required = false, HelpText = "Area path. If not provided, we find it by convention.")]
		public string? AreaPath { get; set; }

		[Option(longName: "area-path-filter", Required = false)]
		public Regex? AreaPathFilter { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetReleaseNotes> logger)
		{
			var releaseNotesReader = new ReleaseNotes(azureDevOps);

			if (Id.HasValue)
			{
				var releaseNotes = await releaseNotesReader.OfWorkItem(Id.Value);
				var html = releaseNotes.Html(retainFirstH1: !HideFirstH1, showTags: !HideTags, allowedTags: NullIfEmpty(AllowedTags));

				if (string.Equals(Output, "html", StringComparison.InvariantCultureIgnoreCase))
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
				var releaseNotes = await releaseNotesReader.OfRecentClosings(Project, Team, IterationPath, IterationPathFilter, AreaPath, AreaPathFilter, take: TakeSprints);

				if (string.Equals(Output, "html", StringComparison.InvariantCultureIgnoreCase))
				{
					var html = releaseNotes.Html(retainFirstH1: !HideFirstH1, showTags: !HideTags, allowedTags: NullIfEmpty(AllowedTags));

					var fileName = $"releasenotes_recentclosings_{DateTime.Now:yyyyMMdd-HHmmss}.html";
					File.WriteAllText(fileName, html);
					Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
				}
				else if (string.Equals(Output, "md", StringComparison.InvariantCultureIgnoreCase))
				{
					var md = releaseNotes.Md();

					if (!string.IsNullOrWhiteSpace(ReplaceByLink))
					{
						var replaceByLinkSplit = ReplaceByLink.Split("=", StringSplitOptions.RemoveEmptyEntries);
						md = Regex.Replace(md, replaceByLinkSplit[0], m => $"[{m.Groups[0].Value.Trim()}]({string.Format(replaceByLinkSplit[1], m.Groups[1].Value.Trim())})");
					}

					if (!string.IsNullOrWhiteSpace(MergeInto))
					{
						if (File.Exists(MergeInto))
						{
							var newHeadings = md.Split("##", StringSplitOptions.RemoveEmptyEntries);
							var lastNewHeading = newHeadings.Last();

							var existingMd = File.ReadAllText(MergeInto);
							var existingHeadings = existingMd.Split("##", StringSplitOptions.RemoveEmptyEntries);
							var olderThanNewHeadings = existingHeadings.Reverse().TakeWhile(existingHeading => !existingHeading.StartsWith(lastNewHeading.Split("\n").First())).Reverse().ToList();

							if (olderThanNewHeadings.Count == existingHeadings.Length)//last new heading is older than oldest existing heading => keep all new headings
							{
								olderThanNewHeadings = [];
							}

							var mergedMd = string.Join("##", newHeadings.Concat(olderThanNewHeadings));

							File.WriteAllText(MergeInto, mergedMd);
							Process.Start(new ProcessStartInfo(new FileInfo(MergeInto).FullName) { UseShellExecute = true });
						}
						else
						{
							File.WriteAllText(MergeInto, md);
							Process.Start(new ProcessStartInfo(new FileInfo(MergeInto).FullName) { UseShellExecute = true });
						}
					}
					else
					{
						var fileName = $"releasenotes_recentclosings_{DateTime.Now:yyyyMMdd-HHmmss}.md";
						File.WriteAllText(fileName, md);
						Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
					}
				}
				else if (string.Equals(Output, "xml", StringComparison.InvariantCultureIgnoreCase))
				{
					var xml = releaseNotes.Xml();

					if (!string.IsNullOrWhiteSpace(MergeInto))
					{
						File.WriteAllText(MergeInto, xml);
						Process.Start(new ProcessStartInfo(new FileInfo(MergeInto).FullName) { UseShellExecute = true });
					}
					else
					{
						var fileName = $"releasenotes_recentclosings_{DateTime.Now:yyyyMMdd-HHmmss}.xml";
						File.WriteAllText(fileName, xml);
						Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
					}
				}
				else
				{
					releaseNotes.ConsoleOut();
				}
			}
		}

		static T[]? NullIfEmpty<T>(IEnumerable<T> ts)
		{
			var array = ts.ToArray();
			return array.Length == 0 ? null : array;
		}
	}

	public static class ReleaseNotesExtensions
	{
		public static void ConsoleOut(this ReleaseNotes.IReleaseNotesWorkItems releaseNotes)
		{
			var groups = (releaseNotes as ReleaseNotes.GroupedReleaseNotesWorkItems)?.GroupedWorkItems;
			foreach (var group in groups.NeverNull().Reverse())
			{
				AnsiConsole.MarkupLine($"[bold]{group.GroupName}[/]");
				foreach (var workItem in group.WorkItems)
				{
					var panel = new Panel(workItem.ReleaseNotes);
					panel.Header = new PanelHeader($"#{workItem.Id} {string.Join(", ", workItem.Tags.Except(["ReleaseNotes"]))}") { Justification = Justify.Left };
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
