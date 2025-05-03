using CommandLine;
using Microsoft.Extensions.Logging;
using static ColoredConsole;

namespace popilot.cli.Verbs
{
	[Verb("get-workitem")]
	class GetWorkItem
	{
		[Value(0, MetaName = "work item ID (int)", Required = true)]
		public int Id { get; set; }

		[Option(longName: "raw", Required = false)]
		public bool Raw { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetWorkItem> logger)
		{
			if (Raw)
			{
				Boring(Json((await azureDevOps.GetWorkItemsRaw([Id])).SingleOrDefault()));
			}
			else
			{
				var wi = (await azureDevOps.GetWorkItems([Id])).SingleOrDefault();
				Boring(Json(wi));
				Boring("State Changes");
				Boring(Json(await azureDevOps.GetStateChanges(Id)));

				if (wi != null)
				{
					Boring("Iteration");
					var path = wi.IterationPath.StartsWith(wi.TeamProject) ? wi.IterationPath.Substring(wi.TeamProject.Length + 1) : wi.IterationPath;
					var iterations = await azureDevOps.GetAllIterations(wi.TeamProject, null, path);
					foreach (var i in iterations)
					{
						Console.WriteLine($"{i.Name} ends on {i.Attributes.FinishDate}");
					}
				}
			}
		}
	}
}
