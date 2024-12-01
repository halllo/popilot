using CommandLine;
using Microsoft.Extensions.Logging;

namespace popilot.cli.Verbs
{
	[Verb("get-organizations")]
	class GetOrganizations
	{
		[Value(0, MetaName = "name", HelpText = "Search for name infix", Required = false)]
		public IEnumerable<string> SearchNames { get; set; } = null!;

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetOrganizations> logger, Zendesk zendesk)
		{
			var organizations = await Cached.Do("zendesk_organisations_cached.json", () => zendesk.GetOrganizations().ToListAsync().AsTask());

			if (!SearchNames.Any())
			{
				logger.LogInformation("Loaded {Count} organizations.", organizations.Count);
			}
			else
			{
				var foundOrganizations = organizations
					.Where(o => SearchNames.Any(searchTerm => o.Name.Contains(searchTerm, StringComparison.InvariantCultureIgnoreCase)))
					.ToList();

				Json.Out(foundOrganizations.Select(o => new { o.Id, o.Name }));
				logger.LogInformation("Found {Count} organizations.", foundOrganizations.Count);
			}
		}
	}
}
