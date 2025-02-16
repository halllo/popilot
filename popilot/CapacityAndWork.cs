using Microsoft.TeamFoundation.Work.WebApi;

namespace popilot
{
	public class CapacityAndWork
	{
		private readonly AzureDevOps azureDevOps;

		public CapacityAndWork(AzureDevOps azureDevOps)
		{
			this.azureDevOps = azureDevOps;
		}

		public enum WorkerDetector
		{
			ChangedBy,
			AssignedTo,
			ChangedByAssignedTo,
		}

		public async Task<SprintCapacityAndWork> OfCurrentSprint(string? project, string? team, Func<AzureDevOps.IWorkItemDto, bool> workItemFilter, WorkerDetector workerDetector)
		{
			var currentIteration = await azureDevOps.GetCurrentIteration(project, team);
			return await OfSprint(project, team, currentIteration, workItemFilter, workerDetector);
		}

		public async Task<SprintCapacityAndWork> OfSprint(string? project, string? team, TeamSettingsIteration iteration, Func<AzureDevOps.IWorkItemDto, bool> workItemFilter, WorkerDetector workerDetector)
		{
			var capacities = await azureDevOps.GetCapacities(project, team, iteration);

			var sprintDays = EnumerableEx
				.Generate(iteration.Attributes.StartDate!.Value, i => i != iteration.Attributes.FinishDate!.Value.AddDays(1), i => i.AddDays(1), i => i)
				.Where(i => new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }.Contains(i.DayOfWeek) == false);

			var sprintDaysBeforeToday = sprintDays.TakeWhile(d => d < DateTime.Today.Date);

			var workItems = await azureDevOps.GetWorkItems(iteration);
			var workUpdates = await workItems
				.Where(workItemFilter)
				.ToAsyncEnumerable()
				.SelectManyAwait(async i =>
				{
					var history = await azureDevOps.GetWorkItemHistory(i.Id);
					return history
						.Where(u => u.Fields != null)
						.Select(u => new WorkUpdate
						{
							WorkItem = i,
							ChangedBy = u.RevisedBy.DisplayName,
							ChangedDate = u.Fields.TryGetValue("System.ChangedDate", out var changedDate) ? (DateTime?)changedDate.NewValue : default,
							CompletedWorkDelta = u.Fields.TryGetValue("Microsoft.VSTS.Scheduling.CompletedWork", out var completed) ? (((double?)completed.NewValue ?? 0.0) - ((double?)completed.OldValue ?? 0.0)) : default,
							RemainingWorkDelta = u.Fields.TryGetValue("Microsoft.VSTS.Scheduling.RemainingWork", out var remaining) ? (((double?)remaining.NewValue ?? 0.0) - ((double?)remaining.OldValue ?? 0.0)) : default,
						})
						.Where(u => u.CompletedWorkDelta != 0.0 || u.RemainingWorkDelta != 0.0)
						.Where(u => u.ChangedDate.HasValue && sprintDays.Contains(u.ChangedDate.Value.Date))
						.ToAsyncEnumerable()
						;
				})
				.ToListAsync();

			var teamMembers = capacities.TeamMembers
				.Select(teamMember =>
				{
					var teamMemberDisplayName = teamMember.TeamMember.DisplayName;

					double?[] capacityOfTeamMember(IEnumerable<DateTime> days) => days
						.Select(sprintDay => teamMember.DaysOff.Contains(sprintDay) ? default(double?) : teamMember.Activities.Sum(a => a.CapacityPerDay))
						.ToArray();

					bool workUpdateFilter(WorkUpdate workUpdate) => workerDetector switch
					{
						WorkerDetector.ChangedBy => workUpdate.ChangedBy == teamMember.TeamMember.DisplayName,
						WorkerDetector.AssignedTo => workUpdate.WorkItem.AssignedTo == teamMember.TeamMember.DisplayName,
						WorkerDetector.ChangedByAssignedTo => workUpdate.ChangedBy == workUpdate.WorkItem.AssignedTo && workUpdate.ChangedBy == teamMember.TeamMember.DisplayName,
						_ => throw new ArgumentOutOfRangeException(nameof(workerDetector)),
					};

					double?[] remainingWorkDelta(IEnumerable<DateTime> days) => days
						.Select(sprintDay => EnumerableEx
							.Return(workUpdates.Where(u => u.ChangedDate!.Value.Date == sprintDay && workUpdateFilter(u)))
							.Select(us => us.ToList())
							.Select(us => us.Count == 0 ? default(double?) : us.Sum(u => u.RemainingWorkDelta))
							.Single()
						)
						.ToArray();

					double?[] completedWorkDelta(IEnumerable<DateTime> days) => days
						.Select(sprintDay => EnumerableEx
							.Return(workUpdates.Where(u => u.ChangedDate!.Value.Date == sprintDay && workUpdateFilter(u)))
							.Select(us => us.ToList())
							.Select(us => us.Count == 0 ? default(double?) : us.Sum(u => u.CompletedWorkDelta))
							.Single()
						)
						.ToArray();

					var com = completedWorkDelta(sprintDaysBeforeToday).ToArray();
					var rem = remainingWorkDelta(sprintDaysBeforeToday).ToArray();
					var days = sprintDays
						.Zip(capacityOfTeamMember(sprintDays)).Zip(remainingWorkDelta(sprintDays)).Zip(completedWorkDelta(sprintDays))
						.Select(zipped => new SprintCapacityAndWork.TeamMember.Day
						{
							SprintDay = zipped.First.First.First,
							Capacity = zipped.First.First.Second,
							RemainingWorkDelta = zipped.First.Second,
							CompletedWorkDelta = zipped.Second,
							IsDayOff = teamMember.DaysOff.Contains(zipped.First.First.First)
						})
						.ToArray();

					return new SprintCapacityAndWork.TeamMember
					{
						DisplayName = teamMemberDisplayName,
						TotalCapacity = capacityOfTeamMember(sprintDays).Sum(),
						CapacityUntilToday = capacityOfTeamMember(sprintDaysBeforeToday).Sum(),
						CompletedWorkDeltaUntilToday = completedWorkDelta(sprintDaysBeforeToday).Sum(),
						RemainingWorkDeltaUntilToday = remainingWorkDelta(sprintDaysBeforeToday).Sum(),
						Days = days,
					};
				})
				.ToArray();

			return new SprintCapacityAndWork
			{
				Start = iteration.Attributes.StartDate!.Value,
				End = iteration.Attributes.FinishDate!.Value,
				Days = sprintDays.ToArray(),
				Path = iteration.Path,
				TeamMembers = teamMembers,
			};
		}

		private class WorkUpdate
		{
			public AzureDevOps.IWorkItemDto WorkItem { get; internal set; } = null!;
			public string ChangedBy { get; internal set; } = null!;
			public DateTime? ChangedDate { get; internal set; }
			public double CompletedWorkDelta { get; internal set; }
			public double RemainingWorkDelta { get; internal set; }
		}

		public class SprintCapacityAndWork
		{
			public DateTime Start { get; internal set; }
			public DateTime End { get; internal set; }
			public DateTime[] Days { get; internal set; } = null!;
			public string Path { get; internal set; } = null!;
			public TeamMember[] TeamMembers { get; internal set; } = null!;

			public class TeamMember
			{
				public string DisplayName { get; internal set; } = null!;
				public double? TotalCapacity { get; internal set; }
				public double? CapacityUntilToday { get; internal set; }
				public double? CompletedWorkDeltaUntilToday { get; internal set; }
				public double? RemainingWorkDeltaUntilToday { get; internal set; }
				public Day[] Days { get; internal set; } = null!;

				public class Day
				{
					public DateTime SprintDay { get; internal set; }
					public double? Capacity { get; internal set; }
					public double? CompletedWorkDelta { get; internal set; }
					public double? RemainingWorkDelta { get; internal set; }
					public bool IsDayOff { get; internal set; }
				}
			}
		}
	}
}
