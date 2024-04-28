using Microsoft.TeamFoundation.Work.WebApi;
using static popilot.AzureDevOps;

namespace popilot
{
	public static class Statistics
	{
		public static SprintStatistics GetSprintStatistics(this IEnumerable<IterationWithWorkItems> iterations) => new SprintStatistics(
			Start: iterations.First().Iteration.Attributes.StartDate!.Value,
			End: iterations.Last().Iteration.Attributes.FinishDate!.Value,
			AllItems: iterations.SelectMany(i => i.WorkItems).Count(),
			ItemsInLastSprint: iterations.Last().WorkItems.Count(),
			ItemsPerSprint: iterations.Average(i => i.WorkItems.Count()),
			AllStoryPoints: iterations.SelectMany(i => i.WorkItems).Sum(i => i.StoryPoints ?? 0),
			StoryPointsInLastSprint: iterations.Last().WorkItems.Sum(i => i.StoryPoints ?? 0),
			StoryPointsPerSprint: iterations.Average(i => i.WorkItems.Sum(i => i.StoryPoints ?? 0)),
			AllStories: iterations.SelectMany(i => i.WorkItems).Count(i => i.Type == "User Story"),
			StoriesInLastSprint: iterations.Last().WorkItems.Count(i => i.Type == "User Story"),
			StoriesPerSprint: iterations.Average(i => i.WorkItems.Count(i => i.Type == "User Story")),
			AllBugs: iterations.SelectMany(i => i.WorkItems).Count(i => i.Type == "Bug"),
			BugsInLastSprint: iterations.Last().WorkItems.Count(i => i.Type == "Bug"),
			BugsPerSprint: iterations.Average(i => i.WorkItems.Count(i => i.Type == "Bug")),
			LastSprintGoalReached: iterations.Last().Iteration.Path switch
			{
				var p when p.EndsWith("👍") => true,
				var p when p.EndsWith("👎") => false,
				_ => null,
			}
		);

		public record SprintStatistics(
			DateTime Start,
			DateTime End,
			int AllItems,
			int ItemsInLastSprint,
			double ItemsPerSprint,
			int AllStoryPoints,
			int StoryPointsInLastSprint,
			double StoryPointsPerSprint,
			int AllStories,
			int StoriesInLastSprint,
			double StoriesPerSprint,
			int AllBugs,
			int BugsInLastSprint,
			double BugsPerSprint,
			bool? LastSprintGoalReached
		);



		public static async Task<IterationStatistics> GetIterationStatistics(this AzureDevOps azureDevOps, string? project, string? team)
		{
			var allSprints = await azureDevOps.GetIterations(project, team);
			var currentSprint = allSprints.Where(i => i.Attributes.TimeFrame == TimeFrame.Current).Single();
			var iterations = allSprints.GroupBy(s => string.Join('\\', s.Path.Split('\\').Reverse().Skip(1).Reverse()));
			var currentIteration = iterations.Single(i => i.Contains(currentSprint));

			var currentIterationWorkItems = await azureDevOps.GetWorkItems(currentIteration);
			var closedWorkItems = currentIterationWorkItems.Where(w => w.State == "Closed");
			var fractionClosedWorkItems = (double)closedWorkItems.Count() / currentIterationWorkItems.Count();
			
			var currentIterationFeatures = (await azureDevOps.GetWorkItemsOfIterationPath(currentIteration.Key)).Where(w => w.Type == "Feature");
			var closedFeatures = currentIterationFeatures.Where(w => w.State == "Closed");
			var fractionClosedFeatures = (double)closedFeatures.Count() / currentIterationFeatures.Count();
			
			var commitedWorkItems = currentIterationWorkItems.Where(w => w.ParentTags?.Contains("committed", StringComparer.InvariantCultureIgnoreCase) ?? false);
			var fractionCommittedWorkItems = (double)commitedWorkItems.Count() / currentIterationWorkItems.Count();
			var fractionCommittedFeatures = (double)currentIterationFeatures.Where(w => w.Tags.Contains("committed", StringComparer.InvariantCultureIgnoreCase)).Count() / currentIterationFeatures.Count();
			
			var previousIteration = iterations.Reverse().SkipWhile(i => i.Key != currentIteration.Key).Skip(1).Take(1).Single();
			var previousIterationWorkItems = await azureDevOps.GetWorkItems(previousIteration);
			var spilloverWorkItems = currentIterationWorkItems.Where(w => w.ParentTags?.Contains("spillover", StringComparer.InvariantCultureIgnoreCase) ?? false);
			var fractionSpilloverWorkItems = (double)spilloverWorkItems.Count() / (spilloverWorkItems.Count() + previousIterationWorkItems.Count());
			
			var spilloverFeatures = currentIterationFeatures.Where(w => w.Tags.Contains("spillover", StringComparer.InvariantCultureIgnoreCase));
			var previousIterationFeatures = (await azureDevOps.GetWorkItemsOfIterationPath(previousIteration.Key)).Where(w => w.Type == "Feature");
			var fractionSpilloverFeatures = (double)spilloverFeatures.Count() / (spilloverFeatures.Count() + previousIterationFeatures.Count());
			
			return new IterationStatistics(
				Name: currentIteration.Key,
				Start: currentIteration.First().Attributes.StartDate!.Value,
				End: currentIteration.Last().Attributes.FinishDate!.Value,
				FractionClosedWorkItems: fractionClosedWorkItems,
				FractionClosedFeatures: fractionClosedFeatures,
				FractionCommittedWorkItems: fractionCommittedWorkItems,
				FractionCommittedFeatures: fractionCommittedFeatures,
				FractionSpilloverWorkItems: fractionSpilloverWorkItems,
				FractionSpilloverFeatures: fractionSpilloverFeatures,
				PreviousIterationName: previousIteration.Key
			);
		}

		public record IterationStatistics(
			string Name,
			DateTime Start,
			DateTime End,
			double FractionClosedWorkItems,
			double FractionClosedFeatures,
			double FractionCommittedWorkItems,
			double FractionCommittedFeatures,
			double FractionSpilloverWorkItems,
			double FractionSpilloverFeatures,
			string PreviousIterationName
		);
	}
}
