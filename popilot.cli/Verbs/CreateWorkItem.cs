using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace popilot.cli.Verbs
{
	[Verb("create-workitem")]
	class CreateWorkItem
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		[Value(0, MetaName = "title", Required = true)]
		public string Title { get; set; } = null!;

		[Option(longName: "task", Required = false)]
		public bool Task { get; set; }

		[Option(longName: "sprint", Required = true)]
		public string Sprint { get; set; } = null!;

		[Option(longName: "assignee", Required = true)]
		public string Assignee { get; set; } = null!;

		[Option(longName: "effort", Required = true)]
		public int Effort { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<CreateWorkItem> logger)
		{
			var sprints = await azureDevOps.GetIterations(Project, Team);
			var selectedSprint = sprints.Single(s => s.Path.Contains(Sprint, StringComparison.InvariantCultureIgnoreCase));

			var (capacities, daysOff) = await azureDevOps.GetCapacities(Project, Team, selectedSprint);
			var selectedTeamMember = capacities.TeamMembers.Single(m => m.TeamMember.DisplayName.Contains(Assignee, StringComparison.InvariantCultureIgnoreCase));

			var newWorkItem = await azureDevOps.CreateWorkItem(
				project: Project, 
				team: Team, 
				iteration: selectedSprint, 
				type: "Task", 
				title: Title, 
				assignee: selectedTeamMember.TeamMember, 
				effort: Effort,
				parent: null);
			
			AnsiConsole.Write(ColoredConsole.Display2(newWorkItem));
			AnsiConsole.WriteLine();
		}
	}
}
