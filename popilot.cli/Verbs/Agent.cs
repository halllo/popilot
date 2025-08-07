using AgentDo;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;

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

		public async Task<(bool success, string summary)> Do(AzureDevOps azureDevOps, ILogger<Agent> logger, [FromKeyedServices("bedrock")] IAgent agent)
		{
			if (Simulate) logger.LogWarning("Simulation mode.");
			logger.LogInformation("Task: {Task}", Task);
			var agentResult = await agent.Do(
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
						async ([Required]int workItemId, string tag) =>
						{
							if (!Simulate)
							{
								var cancelToken = CancellationToken.None;
								var workItem = (await azureDevOps.GetWorkItems([workItemId], cancelToken)).Single();
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

					Tool.From(toolName: "PlanImplementation", logInputsAndOutputs: false, tool: [Description("Make a plan to fulfill the requirement.")]
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

					Tool.From(
						toolName: "ForEachWorkItem",
						tool: [Description("Perform a task for each work item of the query.")] async (
							[Required]Guid queryId,
							[Description("Can be anything you, the agent, can do. All your tools can also be used in this task.")]string task) =>
						{
							var cancelToken = CancellationToken.None;
							var workItems = await azureDevOps.GetQueryResultsFlat(queryId, Project, Team, cancelToken);
							foreach (var workItem in workItems)
							{
								var subTask = $"Lets work on work item {workItem.Id} \"{workItem.Title}\".\n\n{task}";
								var subAgent = new Agent { Project = Project, Team = Team, Simulate = Simulate, Task = subTask };
								var subAgentResult = await subAgent.Do(azureDevOps, logger, agent);
								if(!subAgentResult.success)
								{
									return $"I failed to perform task for work item {workItem.Id} \"{workItem.Title}\": {subAgentResult.summary}";
								}
							}

							return $"I have successfully performed the task on the {workItems.Count} work items of the query. Consider the task complete for these work items.";
						}),

					Tool.From([Description("Get work item details.")]
						async ([Required]int workItemId) =>
						{
							var cancelToken = CancellationToken.None;
							var workItem = await azureDevOps.GetWorkItem(workItemId, cancelToken);
							return workItem;
						}),
				],
				events: AgentOutput.Events(true));

			return await SummarizeResult(agent, agentResult);
		}

		private async Task<(bool, string)> SummarizeResult(IAgent agent, AgentResult agentResult)
		{
			bool? success = null;
			string? summary = null;
			await agent.Do(
				task: $"Summarize the interaction and determine if it was successful.\n\n{JsonSerializer.Serialize(agentResult)}",
				tools:
				[
					Tool.From([Description("Provide summary.")]
						(bool overallSuccessful, string shortSummary, Tool.Context context) =>
						{
							success = overallSuccessful;
							summary = shortSummary;
							context.Cancelled = true;
						}),
				]);

			return (success ?? false, summary ?? string.Empty);
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
