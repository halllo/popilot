using CommandLine;
using Microsoft.Extensions.Logging;
using NodaTime;
using static ColoredConsole;

namespace popilot.cli.Verbs
{
	[Verb("get-velocity")]
	class GetVelocity
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		[Option(longName: "take", Required = false, HelpText = "number of sprints to take (default: 10)")]
		public int? Take { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetVelocity> logger)
		{
			var pastSprints = await azureDevOps.GetPastIterationsWithCompletedWorkItems(Project, Team, take: Take);
			foreach (var sprint in pastSprints)
			{
				var wis = sprint.WorkItems;
				Print(color: ConsoleColor.White, text: $"\n{sprint.Iteration.Path} (Bugs: {wis.Count(i => i.Type == "Bug")}; UserStories: {wis.Count(i => i.Type == "User Story")}; StoryPoints: {wis.Sum(i => i.StoryPoints ?? 0)})");
				foreach (var workItem in wis)
				{
					Display(workItem);
				}
			}

			var sprints = pastSprints.GetSprintStatistics();
			var pastSprint = pastSprints.First().Iteration;
			logger.LogInformation("Sprint {Name} from {Start:dd.MM.yyyy} to {Finish:dd.MM.yyyy}", pastSprint.Path, pastSprint.Attributes.StartDate, pastSprint.Attributes.FinishDate);
			Info($"{pastSprints.Count()} Sprints");
			Info($"{Period.Between(LocalDateTime.FromDateTime(sprints.Start), LocalDateTime.FromDateTime(sprints.End)).Months} Months");
			Info($"{sprints.AllItems} WorkItems");
			Info($"{sprints.AllBugs} Bugs");
			Info($"{sprints.AllStories} UserStories");
			Info($"{sprints.AllStoryPoints} StoryPoints");
			Info($"{sprints.ItemsPerSprint} WorkItems/Sprint");
			Info($"{sprints.BugsPerSprint} Bugs/Sprint");
			Info($"{sprints.StoriesPerSprint} UserStories/Sprint");
			Info($"{sprints.StoryPointsPerSprint} StoryPoints/Sprint");

			Console.WriteLine();

			var iteration = await azureDevOps.GetIterationStatistics(Project, Team);
			logger.LogInformation("Iteration {Name} from {Start:dd.MM.yyyy} to {Finish:dd.MM.yyyy}", iteration.Name, iteration.Start, iteration.End);
			Info($"{iteration.FractionClosedWorkItems:0%} Closed US,Bugs,Tasks");
			Info($"{iteration.FractionClosedFeatures:0%} Closed Features");
			Info($"{iteration.FractionCommittedWorkItems:0%} Committed US,Bugs,Tasks");
			Info($"{iteration.FractionCommittedFeatures:0%} Committed Features");
			Info($"{iteration.FractionSpilloverWorkItems:0%} Spillover US,Bugs,Tasks from {iteration.PreviousIterationName}");
			Info($"{iteration.FractionSpilloverFeatures:0%} Spillover Features from {iteration.PreviousIterationName}");
			Info($"{iteration.FractionNonRoadmapWork:0%} non-roadmap work");
			
			Json.Out(iteration.SprintWorks);
		}
	}
}
