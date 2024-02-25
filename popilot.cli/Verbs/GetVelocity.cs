using CommandLine;
using Microsoft.Extensions.Logging;
using NodaTime;
using static ColoredConsole;

namespace popilot.Verbs
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
			var iterations = await azureDevOps.GetPastIterationsWithCompletedWorkItems(Project, Team, take: Take);
			foreach (var iteration in iterations)
			{
				var wis = iteration.WorkItems;
				Print(color: ConsoleColor.White, text: $"\n{iteration.Iteration.Path} (Bugs: {wis.Count(i => i.Type == "Bug")}; UserStories: {wis.Count(i => i.Type == "User Story")}; StoryPoints: {wis.Sum(i => i.StoryPoints ?? 0)})");
				foreach (var workItem in wis)
				{
					Display(workItem);
				}
			}

			var stats = iterations.GetStatistics();
			Heading($"Statistics (from {iterations.First().Iteration.Attributes.StartDate:dd.MM.yyyy} to {iterations.Last().Iteration.Attributes.FinishDate:dd.MM.yyyy})");
			Info($"{iterations.Count()} Sprints");
			Info($"{Period.Between(LocalDateTime.FromDateTime(stats.Start), LocalDateTime.FromDateTime(stats.End)).Months} Months");
			Info($"{stats.AllItems} WorkItems");
			Info($"{stats.AllBugs} Bugs");
			Info($"{stats.AllStories} UserStories");
			Info($"{stats.AllStoryPoints} StoryPoints");
			Info($"{stats.ItemsPerSprint} WorkItems/Sprint");
			Info($"{stats.BugsPerSprint} Bugs/Sprint");
			Info($"{stats.StoriesPerSprint} UserStories/Sprint");
			Info($"{stats.StoryPointsPerSprint} StoryPoints/Sprint");
		}
	}
}
