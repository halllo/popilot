using AgentDo;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace popilot.cli.Verbs
{
	[Verb("agent")]
	class Agent
	{
		[Value(0, MetaName = "task", Required = true)]
		public string Task { get; set; } = null!;

		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<Agent> logger, [FromKeyedServices("bedrock")] IAgent agent)
		{
			logger.LogInformation("Task: {Task}", Task);
			await agent.Do(
				task: Task,
				tools:
				[
					Tool.From([Description("Get sprints.")]
						async () => 
						{
							var sprints = await azureDevOps.GetIterations(Project, Team);
							return sprints.Select(sprint => new {
								sprintName = sprint.Path,
								timeframe = sprint.Attributes.TimeFrame.ToString(),
								from = sprint.Attributes.StartDate?.ToString("dd.MM.yyyy"),
								to = sprint.Attributes.FinishDate?.ToString("dd.MM.yyyy"),
							}).ToArray();
						}),

					Tool.From([Description("Get current date.")]
						() =>
						{
							return DateTime.Now.ToString("dd.MM.yyyy");
						}),

					Tool.From([Description("Get sprint capacity.")]
						async (string sprintName, string teamMember) =>
						{
							var sprints = await azureDevOps.GetIterations(Project, Team);
							var selectedSprint = sprints.Single(s => s.Path == sprintName);
							var capacityAndWork = new CapacityAndWork(azureDevOps);
							var capacities = await capacityAndWork.OfSprint(Project, Team, selectedSprint, workItemFilter: w => false, CapacityAndWork.WorkerDetector.AssignedTo);
							var selectedTeamMember = capacities.TeamMembers.Single(m => m.DisplayName.Contains(teamMember, StringComparison.InvariantCultureIgnoreCase));
							return new {
								teamMember = selectedTeamMember.DisplayName,
								totalCapacityInHours = selectedTeamMember.TotalCapacity.ToString(),
							};
						}),

					Tool.From([Description("Create task.")]
						async (string sprintName, string teamMember, string/*int?*/ hours, string taskDescription) =>
						{
							var sprints = await azureDevOps.GetIterations(Project, Team);
							var selectedSprint = sprints.Single(s => s.Path == sprintName);

							var capacities = await azureDevOps.GetCapacities(Project, Team, selectedSprint);
							var selectedTeamMember = capacities.TeamMembers.Single(m => m.TeamMember.DisplayName.Contains(teamMember, StringComparison.InvariantCultureIgnoreCase));

							var newWorkItem = await azureDevOps.CreateWorkItem(
								project: Project,
								team: Team,
								iteration: selectedSprint,
								type: "Task",
								title: taskDescription,
								assignee: selectedTeamMember.TeamMember,
								effort: int.Parse(hours));

							return new {
								id = newWorkItem.Id,
								url = newWorkItem.UrlHumanReadable()
							};
						}),
				]);
		}
	}
}
