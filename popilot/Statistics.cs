using static popilot.AzureDevOps;

namespace popilot
{
	public static class Statistics
    {
		public static WorkItemStatistics GetStatistics(this IEnumerable<IterationWithWorkItems> iterations) => new WorkItemStatistics(
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
			BugsPerSprint: iterations.Average(i => i.WorkItems.Count(i => i.Type == "Bug"))
		);

		public record WorkItemStatistics(
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
			double BugsPerSprint
		);
    }
}
