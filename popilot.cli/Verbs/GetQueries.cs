using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using QuickGraph.Algorithms;
using Spectre.Console;
using static ColoredConsole;

namespace popilot.cli.Verbs
{
	[Verb("get-queries")]
	class GetQueries
	{
		[Option('p', longName: "project", Required = false)]
		public string? Project { get; set; }
		[Option('t', longName: "team", Required = false)]
		public string? Team { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetQueries> logger)
		{
			var queries = await azureDevOps.GetQueries(Project, Team);
			var root = new QueryHierarchyItem { Name = ".", Children = queries };
			var tree = root.Tree(q => q.Children);

			static string getQueryTypeIcon(QueryType? q) => q switch
			{
				QueryType.Tree => " " + Emoji.Known.DownRightArrow,
				QueryType.Flat => " " + Emoji.Known.DownArrow,
				_ => string.Empty,
			};
			Display(tree, tree.Roots().Single(), displayName: q => new Markup($"[gray]{q.Id}[/]{getQueryTypeIcon(q.QueryType)} {q.Name}"));
			logger.LogInformation("{Count} queries loaded", tree.VertexCount);
		}
	}

	[Verb("get-query")]
	class GetQuery
	{
		[Value(0, MetaName = "query ID (guid)", Required = true)]
		public Guid QueryId { get; set; }

		[Option(longName: "tree", Required = false)]
		public bool Tree { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetQuery> logger)
		{
			if (Tree)
			{
				var (query, tree) = await azureDevOps.GetQueryResults(QueryId);
				if (tree != null)
				{
					logger.LogInformation("{Query} with {Count} work items loaded", query.Name, tree.VertexCount);
					foreach (var root in tree.Roots())
					{
						Display(tree, root, displayName: w => w switch
						{
							null => new Markup($"??? {query.Name}"),
							_ => Display2(w)
						});
					}
				}
				else
				{
					logger.LogError("No work item relations found.");
				}
			}
			else
			{
				var workItems = await azureDevOps.GetQueryResultsFlat(QueryId);
				logger.LogInformation("Found {Count} work items in {Query}", workItems.Count, QueryId);
				foreach (var workItem in workItems)
				{
					AnsiConsole.Write(Display2(workItem));
					AnsiConsole.WriteLine();
				}
			}
		}
	}
}
