﻿using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;

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

		[Option('d', longName: "document", Required = false, HelpText = "Generates a document.")]
		public bool GenerateDocument { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetReleaseNotes> logger)
		{
			var releaseNotesReader = new ReleaseNotes(azureDevOps);

			if (Id.HasValue)
			{
				var releaseNotes = await releaseNotesReader.OfWorkItem(Id.Value);
				var html = releaseNotes.Html(retainFirstH1: !HideFirstH1, showTags: !HideTags, allowedTags: NullIfEmpty(AllowedTags));

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
				var releaseNotes = await releaseNotesReader.OfCurrentOrLastSprints(Project, Team);

				if (GenerateDocument)
				{
					var html = releaseNotes.Html(retainFirstH1: !HideFirstH1, showTags: !HideTags, allowedTags: NullIfEmpty(AllowedTags));

					var fileName = $"releasenotes_lastsprints_{DateTime.Now:yyyyMMdd-HHmmss}.html";
					File.WriteAllText(fileName, html);
					Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
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
			var iterations = (releaseNotes as ReleaseNotes.FlatReleaseNotesWorkItems)?.IterationsWithWorkItems;
			foreach (var iteration in iterations.NeverNull().Reverse())
			{
				AnsiConsole.MarkupLine($"[bold]{iteration.Iteration.Attributes.FinishDate:dd.MM.yyyy}[/] {iteration.Iteration.Path}");
				foreach (var workItem in iteration.WorkItems)
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
