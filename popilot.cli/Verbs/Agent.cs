using AgentDo;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace popilot.cli.Verbs
{
	[Verb("agent")]
	class Agent
	{
		[Value(0, MetaName = "task", Required = true)]
		public string Task { get; set; } = null!;

		[Option('s', longName: "simulation", Required = false, HelpText = "Only simulate, dont actually change anything.")]
		public bool Simulate { get; set; }

		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<Agent> logger, [FromKeyedServices("bedrock")] IAgent agent)
		{
			if (Simulate) logger.LogWarning("Simulation mode.");
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

					Tool.From([Description("Get current date.")] () => DateTime.Now.ToString("dd.MM.yyyy")),

					Tool.From(toolName: "GetSprintCapacity", tool: [Description("Get sprint capacity. If no team member is passed it returns the capacity for the entire team.")]
						async ([Required]string sprintName, string? teamMember) =>
						{
							var normalizedSprintName = sprintName.Replace("\\\\", "\\");
							var sprints = await azureDevOps.GetIterations(Project, Team);
							var selectedSprint = sprints.Single(s => s.Path == normalizedSprintName);
							var capacityAndWork = new CapacityAndWork(azureDevOps);
							var capacities = await capacityAndWork.OfSprint(Project, Team, selectedSprint, workItemFilter: w => false, CapacityAndWork.WorkerDetector.AssignedTo);

							if (teamMember != null) {
								var selectedTeamMember = capacities.TeamMembers.Single(m => m.DisplayName.Contains(teamMember, StringComparison.InvariantCultureIgnoreCase));
								return new {
									teamMember = selectedTeamMember.DisplayName,
									totalCapacityInHours = selectedTeamMember.TotalCapacity.ToString(),
								};
							} else {
								return new {
									teamMember = "all",
									totalCapacityInHours = capacities.TeamMembers.Sum(m => m.TotalCapacity).ToString(),
								};
							}
						}),

					Tool.From([Description("Create task.")]
						async ([Required]string sprintName, [Required]string taskDescription, string? teamMember, string?/*decimal?*/ hours, string?/*int?*/ parent) =>
						{
							if (Simulate) {
								var id = new Random().Next();
								return new {
									id,
									url = $"http://id{id}"
								};
							}

							var normalizedSprintName = sprintName.Replace("\\\\", "\\");
							var sprints = await azureDevOps.GetIterations(Project, Team);
							var selectedSprint = sprints.Single(s => s.Path == normalizedSprintName);

							var (capacities, daysOff) = await azureDevOps.GetCapacities(Project, Team, selectedSprint);
							var selectedTeamMember = teamMember == null ? null : capacities.TeamMembers.Single(m => m.TeamMember.DisplayName.Contains(teamMember, StringComparison.InvariantCultureIgnoreCase));

							var newWorkItem = await azureDevOps.CreateWorkItem(
								project: Project,
								team: Team,
								iteration: selectedSprint,
								type: "Task",
								title: taskDescription,
								assignee: selectedTeamMember?.TeamMember,
								effort: string.IsNullOrWhiteSpace(hours) ? default(decimal?) : decimal.Parse(hours, NumberStyles.Any, CultureInfo.InvariantCulture),
								parent: string.IsNullOrWhiteSpace(parent) ? default(int?) : int.Parse(parent));

							return new {
								id = newWorkItem.Id,
								url = newWorkItem.UrlHumanReadable()
							};
						}),
				]);
		}
	}
}
