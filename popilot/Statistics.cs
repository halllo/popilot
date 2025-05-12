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

			bool childOfParentFeature(IWorkItemDto w) =>
				(   w.Type == "User Story"
				|| (w.Type == "Bug" && w.ParentType == "Feature")
				|| (w.Type == "Task" && w.ParentType == "Feature")
				)
				&&
				w.State != "Removed";


			var currentIterationWorkItems = await azureDevOps.GetWorkItems(currentIteration, childOfParentFeature);
			var closedWorkItems = currentIterationWorkItems.Where(w => w.State == "Closed");
			var fractionClosedWorkItems = (double)closedWorkItems.Count() / currentIterationWorkItems.Count();

			var currentIterationFeatures = (await azureDevOps.GetWorkItemsOfIterationPath(currentIteration.Key)).Where(w => w.Type == "Feature" && w.State != "Removed");
			var closedFeatures = currentIterationFeatures.Where(w => w.State == "Closed");
			var fractionClosedFeatures = (double)closedFeatures.Count() / currentIterationFeatures.Count();

			var committedWorkItems = currentIterationWorkItems.Where(w => w.ParentTags?.Contains("committed", StringComparer.InvariantCultureIgnoreCase) ?? false);
			var fractionCommittedWorkItems = (double)committedWorkItems.Count() / currentIterationWorkItems.Count();

			var committedFeatures = currentIterationFeatures.Where(w => w.Tags.Contains("committed", StringComparer.InvariantCultureIgnoreCase));
			var fractionCommittedFeatures = (double)committedFeatures.Count() / currentIterationFeatures.Count();

			var closedCommittedWorkItems = committedWorkItems.Where(w => w.State == "Closed");
			var fractionClosedCommittedWorkItems = (double)closedCommittedWorkItems.Count() / committedWorkItems.Count();
			var fractionClosedCommittedFeatures = (double)committedFeatures.Where(w => w.State == "Closed").Count() / committedFeatures.Count();

			var previousIteration = iterations.Reverse().SkipWhile(i => i.Key != currentIteration.Key).Skip(1).Take(1).SingleOrDefault();
			var previousIterationWorkItems = previousIteration == null ? [] : await azureDevOps.GetWorkItems(previousIteration, childOfParentFeature);
			var spilloverWorkItems = currentIterationWorkItems.Where(w => w.ParentTags?.Contains("spillover", StringComparer.InvariantCultureIgnoreCase) ?? false);
			var fractionSpilloverWorkItems = (double)spilloverWorkItems.Count() / (spilloverWorkItems.Count() + previousIterationWorkItems.Count());

			var spilloverFeatures = currentIterationFeatures.Where(w => w.Tags.Contains("spillover", StringComparer.InvariantCultureIgnoreCase));
			var previousIterationFeatures = previousIteration == null ? [] : (await azureDevOps.GetWorkItemsOfIterationPath(previousIteration.Key)).Where(w => w.Type == "Feature" && w.State != "Removed");
			var fractionSpilloverFeatures = (double)spilloverFeatures.Count() / (spilloverFeatures.Count() + previousIterationFeatures.Count());

			var worked = await azureDevOps.GetWorkItems(currentIteration, w
				=> (w.Type == "Bug" || w.Type == "Task")
				&& w.State == "Closed"
				&& w.RootParentTitle != null
				&& ((w.Reason != "Obsolete" && w.Reason != "Cut") || (w.CompletedWork ?? 0.0) > 0)
				&& (w.CompletedWork != null || w.OriginalEstimate != null));

			var nonRoadmapWorkParentTitle = azureDevOps.options.Value.NonRoadmapWorkParentTitle;
			var workPerSprint = worked
				.GroupBy(i => i.IterationPath)
				.Select(iw => new
				{
					iteration = iw.Key,
					nonRoadmapOrRoadmapWork = iw.GroupBy(i => !string.IsNullOrWhiteSpace(nonRoadmapWorkParentTitle) ? (i.RootParentTitle?.Contains(nonRoadmapWorkParentTitle) ?? true) : false)
				})
				.Select(iw => new SprintWork
				{
					Name = iw.iteration,
					NonRoadmapWork = iw.nonRoadmapOrRoadmapWork.SingleOrDefault(g => g.Key == true)?.Sum(i => i.CompletedWork ?? i.OriginalEstimate ?? 0.0) ?? 0.0,
					RoadmapWork = iw.nonRoadmapOrRoadmapWork.SingleOrDefault(g => g.Key == false)?.Sum(i => i.CompletedWork ?? i.OriginalEstimate ?? 0.0) ?? 0.0,
				})
				.ToArray();

			var totalWork = workPerSprint.Sum(s => s.RoadmapWork + s.NonRoadmapWork);
			var fractionNonRoadmapWork = totalWork > 0 ? workPerSprint.Sum(s => s.NonRoadmapWork) / totalWork : 0;

			return new IterationStatistics(
				Name: currentIteration.Key,
				Start: currentIteration.First().Attributes.StartDate!.Value,
				End: currentIteration.Last().Attributes.FinishDate!.Value,
				FractionClosedWorkItems: fractionClosedWorkItems,
				FractionClosedFeatures: fractionClosedFeatures,
				FractionCommittedWorkItems: fractionCommittedWorkItems,
				FractionCommittedFeatures: fractionCommittedFeatures,
				FractionClosedCommittedWorkItems: fractionClosedCommittedWorkItems,
				FractionClosedCommittedFeatures: fractionClosedCommittedFeatures,
				FractionSpilloverWorkItems: fractionSpilloverWorkItems,
				FractionSpilloverFeatures: fractionSpilloverFeatures,
				PreviousIterationName: previousIteration?.Key ?? string.Empty,
				FractionNonRoadmapWork: fractionNonRoadmapWork,
				SprintWorks: workPerSprint
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
			double FractionClosedCommittedWorkItems,
			double FractionClosedCommittedFeatures,
			double FractionSpilloverWorkItems,
			double FractionSpilloverFeatures,
			string PreviousIterationName,
			double FractionNonRoadmapWork,
			SprintWork[] SprintWorks
		);

		public class SprintWork
		{
			public string Name { get; set; } = null!;
			public double NonRoadmapWork { get; set; }
			public double RoadmapWork { get; set; }
		}
	}
}
