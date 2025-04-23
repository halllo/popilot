using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Text;

namespace popilot.cli.Verbs
{
	[Verb("get-repositories")]
	class GetRepositories
	{
		[Option('d', longName: "document", Required = false, HelpText = "Generates a document.")]
		public bool GenerateDocument { get; set; }

		[Value(0, MetaName = "filter", Required = false)]
		public string? Filter { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetRepositories> logger)
		{
			StringBuilder stringBuilder = new();
			stringBuilder.AppendLine("# Projects & Repositories");
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("This document contains all projects (##) with their repositories (###).");
			stringBuilder.AppendLine();

			int repositoriesCount = 0;
			var projects = await azureDevOps.GetProjects();
			foreach (var project in projects
				.OrderBy(p => p.name)
				.Where(p => string.IsNullOrEmpty(Filter) || p.name.Contains(Filter, StringComparison.InvariantCultureIgnoreCase)))
			{
				var repositories = await azureDevOps.gitClient!.GetRepositoriesAsync(project.id);
				repositoriesCount += repositories.Count;

				AnsiConsole.MarkupLine($"[gray]{project.id}[/] [bold]{project.name}[/] [gray]{repositories.Count} repositories[/]");
				stringBuilder.AppendLine($"## {project.name}");
				stringBuilder.AppendLine();
				stringBuilder.AppendLine($"Project ID: {project.id}");
				stringBuilder.AppendLine();

				foreach (var repository in repositories.Where(r => r.IsDisabled != true && r.IsInMaintenance != true).OrderBy(r => r.Name))
				{
					AnsiConsole.MarkupLine($"  [link={repository.WebUrl}]{repository.Name}[/] ({repository.DefaultBranch}) [cyan]{repository.RemoteUrl}[/]");
					stringBuilder.AppendLine($"### <{repository.WebUrl}>");
					stringBuilder.AppendLine();
					stringBuilder.AppendLine($"Repository ID: {repository.Id}");
					stringBuilder.AppendLine($"Name: {repository.Name}");
					stringBuilder.AppendLine($"Size: {repository.Size}");
					stringBuilder.AppendLine($"DefaultBranch: {repository.DefaultBranch}");
					stringBuilder.AppendLine($"RemoteUrl: <{repository.RemoteUrl}>");
					stringBuilder.AppendLine();
				}

				AnsiConsole.WriteLine();
			}

			AnsiConsole.MarkupLine($"{repositoriesCount} repositories in {projects.Count} projects.");

			if (GenerateDocument)
			{
				string fileName = $"repositories_{Filter}_{DateTime.Now:yyyyMMdd-HHmmss}.md";
				File.WriteAllText(fileName, stringBuilder.ToString());
				Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
			}
		}
	}
}
