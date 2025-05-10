using CommandLine;
using Microsoft.Extensions.Logging;

namespace popilot.cli.Verbs
{
	[Verb("get-sca")]
	class GetSoftwareCompositionAnalysis
	{
		public async Task Do(Blackduck blackduck, ILogger<GetSoftwareCompositionAnalysis> logger)
		{
			var projects = blackduck.GetProjects();
			int projectCount = 0;
			await foreach (var project in projects)
			{
				Console.WriteLine($"{++projectCount} {project.name}");
			}

			logger.LogInformation("Found {ProjectCount} projects", projectCount);
		}
	}
}