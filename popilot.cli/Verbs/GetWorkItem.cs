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
				Boring(Json((await azureDevOps.GetWorkItemsRaw(new[] { Id })).SingleOrDefault()));
			}
			else
			{
				Boring(Json((await azureDevOps.GetWorkItems(new[] { Id })).SingleOrDefault()));
				Boring("State Changes");
				Boring(Json(await azureDevOps.GetStateChanges(Id)));
			}
		}
	}
}
