using CommandLine;
using Microsoft.Extensions.Logging;

namespace popilot.cli.Verbs
{
    [Verb("get-projects")]
	class GetProjects
	{
		public async Task Do(AzureDevOps azureDevOps, ILogger<GetProjects> logger)
		{
			var projects = await azureDevOps.GetProjects();
			foreach (var project in projects)
			{
				Console.WriteLine($"{project.id} {project.name}");
			}
		}
	}
}
