using CommandLine;
using Microsoft.Extensions.Logging;
using static ColoredConsole;

namespace popilot.cli.Verbs
{
	[Verb("get-sprints")]
	class GetSprints
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetSprints> logger)
		{
			var iterations = await azureDevOps.GetIterations(Project, Team);
			foreach (var iteration in iterations)
			{
				Info(iteration.Name, sameLine: true);
				Boring($" {iteration.Attributes.TimeFrame}; from {iteration.Attributes.StartDate:dd.MM.yyyy} to {iteration.Attributes.FinishDate:dd.MM.yyyy}");
			}
			logger.LogInformation("{Itertions} loaded", iterations.Count);
		}
	}

	[Verb("get-current-sprint")]
	class GetSprint
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }
		[Option('o', longName: "output", Required = false)]
		public string? Output { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetSprint> logger)
		{
			var currentSprint = await azureDevOps.GetCurrentIteration(Project, Team);
			logger.LogInformation("Sprint {Name} from {Start:dd.MM.yyyy} to {Finish:dd.MM.yyyy}", currentSprint.Name, currentSprint.Attributes.StartDate, currentSprint.Attributes.FinishDate);

			var workItems = (await azureDevOps.GetWorkItemsOfIteration(currentSprint))
				.OrderBy(i => i.StackRank)
				.Where(i => new[] { "Bug", "User Story" }.Contains(i.Type))
				.ToList();

			var workItemsProjected = workItems.Select(i => new
			{
				i.Id,
				i.Title,
				i.State,
				Topic = i.ParentTitle,
				i.Tags,
			});

			switch (Output)
			{
				case "json":
					{
						Boring(Json(workItemsProjected, indented: false));
						break;
					}
				case "json+pretty":
					{
						Boring(Json(workItemsProjected, indented: true));
						break;
					}
				default:
					{
						foreach (var workItem in workItems)
						{
							Display(workItem);
						}
						break;
					}
			}
		}
	}
}
