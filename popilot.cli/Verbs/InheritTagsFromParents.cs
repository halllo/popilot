using CommandLine;
using Microsoft.Extensions.Logging;
using static ColoredConsole;

namespace popilot.cli.Verbs
{
	[Verb("inherit-tags-from-parents")]
	class InheritTagsFromParents
	{
		[Value(0, MetaName = "query ID (guid)", Required = true)]
		public Guid QueryId { get; set; }

		[Option(longName: "tags", HelpText = "Comma separated list of tags to inherit", Required = true)]
		public string? RelevantTags { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<InheritTagsFromParents> logger)
		{
			var relevantTags = RelevantTags?.Split(",", StringSplitOptions.RemoveEmptyEntries) ?? [];
			var items = await azureDevOps.GetQueryResultsFlat(QueryId);
			logger.LogInformation($"Found {items.Count} items in query {QueryId}");
			foreach (var item in items)
			{
				Display(item);
				var chilRelevantTags = item.Tags.Intersect(relevantTags, InvariantCultureIgnoreCaseComparer.Instance).ToArray();
				if (chilRelevantTags.Any())
				{
					logger.LogInformation("Already has relevant tag {@Tag}. Skipping.", chilRelevantTags);
					continue;
				}
				else
				{
					var relevantParentTags = item.Parents.SelectMany(p => p.Tags).Intersect(relevantTags, InvariantCultureIgnoreCaseComparer.Instance).ToArray();
					if (!relevantParentTags.Any())
					{
						logger.LogInformation("Parents don't have relevant tags to inherit. Skipping.");
						continue;
					}
					else
					{
						var relevantTag = relevantParentTags.First();
						logger.LogInformation("Inheriting tag {Tag} from parents {@Parents}...", relevantTag, item.Parents.Select(p => p.Id).ToArray());
						await azureDevOps.AddTag(item.Id, relevantTag, CancellationToken.None);
					}
				}
			}
		}
	}
}
