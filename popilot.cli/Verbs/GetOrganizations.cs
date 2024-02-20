using CommandLine;
using Microsoft.Extensions.Logging;

namespace popilot.cli.Verbs
{
	[Verb("get-organizations")]
	class GetOrganizations
	{
		[Value(0, MetaName = "name", HelpText = "Search for name infix", Required = false)]
		public IEnumerable<string> SearchNames { get; set; } = null!;

		[Option(longName: "organizationfield", Required = false)]
		public string? OrganizationField { get; set; }

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
					.Select(o => new
					{
						o.Id,
						o.Name,
						OrganizationField = OrganizationField != null ? o.GetOrgField(OrganizationField) : null,
					})
					.ToList();

				//foreach (var foundOrganization in foundOrganizations)
				//{
				//	Console.WriteLine($"\"{foundOrganization.OrganizationField}\",");
				//}

				Json.Out(foundOrganizations);
				logger.LogInformation("Found {Count} organizations.", foundOrganizations.Count);
			}
		}
	}
}
