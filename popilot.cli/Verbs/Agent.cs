using AgentDo;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
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
							var selectedTeamMember = teamMember == null ? null : capacities.TeamMembers.SingleOrDefault(m => m.TeamMember.DisplayName.Contains(teamMember, StringComparison.InvariantCultureIgnoreCase));

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

					Tool.From([Description("Add tag.")]
						async ([Required]string workItemId, string tag) =>
						{
							if (!Simulate)
							{
								var cancelToken = CancellationToken.None;
								var workItem = (await azureDevOps.GetWorkItems([int.Parse(workItemId)], cancelToken)).Single();
								if (workItem.Tags.Contains(tag))
								{
									await azureDevOps.AddTag(workItem.Id, tag, cancelToken);
								}
							}
							return new
							{
								id = workItemId,
								tag
							};
						}),

					Tool.From(toolName: "AnalyzeRequirement", logInputsAndOutputs: false, tool: [Description("Analyze the requirement.")]
						async ([Required]string requirement, Tool.Context outerCtx) =>
						{
							AnalyzedRequirementPlan? rememberedPlan = null;
							await agent.Do(
								task: $"""
									Create a detailed plan to implement and test this requirement: \"{requirement}\".
									Here is everything the user has written so far: {string.Concat(outerCtx.GetMessages().Where(m => m.Role == "user").Select(m => m.Text))}
									""",
								tools:
								[
									Tool.From(toolName: "ask", logInputsAndOutputs: false, tool: [Description("Clarify something by asking the product owner a specific question.")](string specificQuestion, Tool.Context analysisCtx) =>
									{
										Console.WriteLine(specificQuestion);
										var answer = Console.ReadLine();
										return answer;
									}),
									Tool.From(toolName: "present", logInputsAndOutputs: false, tool: [Description("Present the plan.")](AnalyzedRequirementPlan plan, Tool.Context presentCtx) =>
									{
										presentCtx.Cancelled = true;
										Json.Out(plan);
										rememberedPlan = plan;
									}),
								]);

							if (rememberedPlan != null)
							{
								return $"Requirement was successfully analyzed. Then plan was presented to the requestor, so dont summarize it again! (Just so you know, this is the plan: {Json.Of(rememberedPlan)}, but dont talk to the user about it again.)";
							}
							else
							{
								return "Could not analyze requirement.";
							}
						}),
				]);
		}

		class AnalyzedRequirementPlan
		{
			[Description("Short name of the requirement.")]
			public string Title { get; set; } = null!;
			public string? Summary { get; set; }

			[Description("With what meaningful and valuable milestones can we interatively and incrementally reach the realization of the requirement?")]
			public Milestone[] Milestones { get; set; } = null!;

			public class Milestone
			{
				[Description("Short name of the milestone.")]
				public string Title { get; set; } = null!;
				public string? Description { get; set; }

				[Description("What do we have to do before implementation, to ensure the implementation will succeed?")]
				public PreparationActivity[] Preparation { get; set; } = null!;
				public class PreparationActivity
				{
					[Description("Short name of the activity.")]
					public string Title { get; set; } = null!;
					public string? Description { get; set; }
				}

				[Description("What do we actually have to implement to reach the next milestone?")]
				public ImplementationActivity[] Implementation { get; set; } = null!;
				public class ImplementationActivity
				{
					[Description("Short name of the activity.")]
					public string Title { get; set; } = null!;
					public string? Description { get; set; }
				}

				[Description("How can we make sure what we have implemented actually works and gets us to the next milestone?")]
				public TestingActivity[] Testing { get; set; } = null!;
				public class TestingActivity
				{
					[Description("Short name of the activity.")]
					public string Title { get; set; } = null!;
					public string? Description { get; set; }
				}
			}
		}
	}
}
