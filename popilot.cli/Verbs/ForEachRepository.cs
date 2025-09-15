using CommandLine;
using Microsoft.Extensions.Logging;

namespace popilot.cli.Verbs
{
	[Verb("foreach-repository")]
	class ForEachRepository
	{
		[Option(longName: "report", Required = true, HelpText = "Filename to track progress.")]
		public string ReportFile { get; set; } = null!;

		[Value(0, MetaName = "filter", Required = false)]
		public string? Filter { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<ForEachRepository> logger, ILogger<GetRepositories> getRepoLogger)
		{
			string report;
			if (!File.Exists(ReportFile))
			{
				Console.WriteLine("Create new report...");

				var getRepos = new GetRepositories();
				getRepos.Filter = Filter;
				getRepos.GenerateDocument = true;
				getRepos.GeneratedDocumentName = ReportFile;
				await getRepos.Do(azureDevOps, getRepoLogger);
				report = File.ReadAllText(ReportFile);
			}
			else
			{
				Console.WriteLine("Read report...");
				report = File.ReadAllText(ReportFile);
			}

			Console.WriteLine("For each repository...");
			//todo: for each repository do something
		}
	}
}
