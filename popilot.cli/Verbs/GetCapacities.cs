using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace popilot.cli.Verbs
{
	[Verb("get-capacities")]
	class GetCapacities
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		[Option(longName: "workItemType", HelpText = "Task, Bug, Task&Bug (default)", Required = false)]
		public string? WorkItemType { get; set; } = "Task&Bug";

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetCapacities> logger)
		{
			var (currentIteration, capacities) = await azureDevOps.GetCapacities(Project, Team);
			Console.WriteLine($"{currentIteration.Path} ({currentIteration.Attributes.StartDate}-{currentIteration.Attributes.FinishDate})");
			
			var sprintDays = EnumerableEx
				.Generate(currentIteration.Attributes.StartDate!.Value, i => i != currentIteration.Attributes.FinishDate!.Value.AddDays(1), i => i.AddDays(1), i => i)
				.Where(i => new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }.Contains(i.DayOfWeek) == false);

			var sprintDaysBeforeToday = sprintDays.TakeWhile(d => d < DateTime.Today.Date);

			var workItems = await azureDevOps.GetWorkItems(currentIteration);
			var workUpdates = await workItems
				.Where(i => (WorkItemType ?? string.Empty).Contains(i.Type))
				.ToAsyncEnumerable()
				.SelectManyAwait(async i =>
				{
					var history = await azureDevOps.GetWorkItemHistory(i.Id);
					return history
						.Where(u => u.Fields != null)
						.Select(u => new
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


			var table = new Table();
			table.ShowRowSeparators();
			table.AddColumn("[gray]\nΔCapacity ΔCompletedWork ΔRemainingWork[/]");
			table.BorderColor(Color.Grey);

			foreach (var sprintDay in sprintDays)
			{
				if (sprintDay == DateTime.Today)
				{
					table.AddColumn(new TableColumn(new Markup($"[bold]{sprintDay:ddd}\n{sprintDay:dd.MM}[/]")));
				}
				else
				{
					table.AddColumn($"{sprintDay:ddd}\n{sprintDay:dd.MM}");
				}
			}

			foreach (var teamMember in capacities.TeamMembers)
			{
				var teamMemberDisplayName = teamMember.TeamMember.DisplayName;

				double?[] capacityOfTeamMember(IEnumerable<DateTime> days) => days
					.Select(sprintDay => teamMember.DaysOff.Contains(sprintDay) ? default(double?) : teamMember.Activities.Sum(a => a.CapacityPerDay))
					.ToArray();

				double?[] remainingWorkDelta(IEnumerable<DateTime> days) => days
					.Select(sprintDay => EnumerableEx
						.Return(workUpdates.Where(u => u.ChangedBy == teamMemberDisplayName && u.ChangedDate!.Value.Date == sprintDay))
						.Select(us => us.ToList())
						.Select(us => us.Count == 0 ? default(double?) : us.Sum(u => u.RemainingWorkDelta))
						.Single()
					)
					.ToArray();

				double?[] completedWorkDelta(IEnumerable<DateTime> days) => days
					.Select(sprintDay => EnumerableEx
						.Return(workUpdates.Where(u => u.ChangedBy == teamMemberDisplayName && u.ChangedDate!.Value.Date == sprintDay))
						.Select(us => us.ToList())
						.Select(us => us.Count == 0 ? default(double?) : us.Sum(u => u.CompletedWorkDelta))
						.Single()
					)
					.ToArray();

				table.AddRow([
					new Markup($"{teamMemberDisplayName} ({capacityOfTeamMember(sprintDaysBeforeToday).Sum()} {(completedWorkDelta(sprintDaysBeforeToday).Sum() - capacityOfTeamMember(sprintDaysBeforeToday).Sum()).Against(0.0)} {(capacityOfTeamMember(sprintDaysBeforeToday).Sum() + remainingWorkDelta(sprintDaysBeforeToday).Sum()).AgainstInverse(0.0)})"),
					..sprintDays
						.Zip(capacityOfTeamMember(sprintDays)).Zip(remainingWorkDelta(sprintDays)).Zip(completedWorkDelta(sprintDays))
						.Select(zipped => new
						{
							sprintDay = zipped.First.First.First,
							capacity = zipped.First.First.Second,
							remainingWorkDelta = zipped.First.Second,
							completedWorkDelta = zipped.Second
						})
						.Select(zipped => teamMember.DaysOff.Contains(zipped.sprintDay)
							? new Markup(string.Empty)
							: new Markup($"{zipped.capacity} {zipped.completedWorkDelta.Against(zipped.capacity)} {zipped.remainingWorkDelta.AgainstInverse(zipped.capacity)}"))
						.ToArray()
				]);
			}

			AnsiConsole.Write(table);
		}
	}

	static class Capacitied
	{
		public static string Against(this double? workDelta, double? capacity)
		{
			if (workDelta == null) return string.Empty;
			else if (workDelta >= capacity) return $"[green]{workDelta}[/]";
			else if (workDelta < capacity) return $"[red]{workDelta}[/]";
			else return string.Empty;
		}

		public static string AgainstInverse(this double? workDelta, double? capacity)
		{
			if (workDelta == null) return string.Empty;
			else if (workDelta * -1 >= capacity) return $"[green]{workDelta}[/]";
			else if (workDelta * -1 < capacity) return $"[red]{workDelta}[/]";
			else return string.Empty;
		}
	}
}
