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

		[Option(longName: "workerDetector", HelpText = "AssignedTo, ChangedBy, ChangedByAssignedTo (default)", Required = false)]
		public CapacityAndWork.WorkerDetector WorkerDetector { get; set; } = CapacityAndWork.WorkerDetector.ChangedByAssignedTo;

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetCapacities> logger)
		{
			var capacityAndWork = new CapacityAndWork(azureDevOps);
			var currentSprint = await capacityAndWork.OfCurrentSprint(
				project: Project,
				team: Team,
				workItemFilter: i => (WorkItemType ?? string.Empty).Contains(i.Type),
				workerDetector: WorkerDetector);

			Console.WriteLine($"{currentSprint.Path} ({currentSprint.Start}-{currentSprint.End})");

			var table = new Table();
			table.ShowRowSeparators();
			table.AddColumn("[gray]\nΔCapacity ΔCompletedWork ΔRemainingWork[/]");
			table.BorderColor(Color.Grey);

			foreach (var sprintDay in currentSprint.Days)
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

			foreach (var teamMember in currentSprint.TeamMembers)
			{
				table.AddRow([
					new Markup($"{teamMember.DisplayName} ({teamMember.CapacityUntilToday} {(teamMember.CompletedWorkDeltaUntilToday - teamMember.CapacityUntilToday).Against(0.0)} {(teamMember.CapacityUntilToday + teamMember.RemainingWorkDeltaUntilToday).AgainstInverse(0.0)})"),
					..teamMember.Days
						.Select(day => new Markup($"{(day.IsDayOff ? " " : day.Capacity)} {day.CompletedWorkDelta.Against(day.Capacity)} {day.RemainingWorkDelta.AgainstInverse(day.Capacity)}"))
						.ToArray()
				]);
			}

			AnsiConsole.Write(table);
		}
	}

	static class CapacitiedFormatting
	{
		public static string Against(this double? workDelta, double? capacity)
		{
			if (workDelta == null) return string.Empty;
			else if (workDelta >= (capacity ?? 0)) return $"[green]{workDelta}[/]";
			else if (workDelta < (capacity ?? 0)) return $"[red]{workDelta}[/]";
			else return string.Empty;
		}

		public static string AgainstInverse(this double? workDelta, double? capacity)
		{
			if (workDelta == null) return string.Empty;
			else if (workDelta * -1 >= (capacity ?? 0)) return $"[green]{workDelta}[/]";
			else if (workDelta * -1 < (capacity ?? 0)) return $"[red]{workDelta}[/]";
			else return string.Empty;
		}
	}
}
