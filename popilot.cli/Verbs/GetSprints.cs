﻿using CommandLine;
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
			var sprints = await azureDevOps.GetIterations(Project, Team);
			foreach (var sprint in sprints)
			{
				Info(sprint.Name, sameLine: true);
				Boring($" {sprint.Attributes.TimeFrame}; from {sprint.Attributes.StartDate:dd.MM.yyyy} to {sprint.Attributes.FinishDate:dd.MM.yyyy}");
			}
			logger.LogInformation("{Sprints} loaded", sprints.Count);
		}
	}

	[Verb("get-all-sprints")]
	class GetAllSprints
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetAllSprints> logger)
		{
			var sprints = await azureDevOps.GetAllIterations(Project, Team);

			foreach (var sprint in sprints)
			{
				Info(sprint.Name, sameLine: true);
				Boring($" {sprint.Attributes.TimeFrame}; from {sprint.Attributes.StartDate:dd.MM.yyyy} to {sprint.Attributes.FinishDate:dd.MM.yyyy}");
			}
			logger.LogInformation("{Sprints} loaded", sprints.Count);
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

			var workItems = (await azureDevOps.GetWorkItems(currentSprint))
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
