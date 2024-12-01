using CommandLine;
using Microsoft.Extensions.Logging;
using static popilot.Zendesk;

namespace popilot.cli.Verbs
{
	[Verb("get-tickets")]
	class GetTickets
	{
		[Option(longName: "organizationfield", Required = true)]
		public string OrganizationField { get; set; } = null!;

		[Option(longName: "organizationfieldfiltervalues", Required = true)]
		public IEnumerable<string> FilterValues { get; set; } = null!;

		[Option(longName: "customfieldid", Required = true)]
		public long CustomFieldId { get; set; }

		[Option(longName: "customfieldvalue", Required = true)]
		public string? CustomFieldValue { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<GetOrganizations> logger, Zendesk zendesk)
		{
			var organizations = await Cached.Do<List<Zendesk.Organization>>("zendesk_organisations_cached.json", () => throw new NotImplementedException());

			if (!FilterValues.Any())
			{
				logger.LogError("No FilterValues provided");
			}
			else
			{
				var foundOrganizations = await organizations
					.Where(o => FilterValues.Any(filterValue => o.GetOrgField(OrganizationField)?.Contains(filterValue, StringComparison.InvariantCultureIgnoreCase) ?? false))
					.ToAsyncEnumerable()
					.SelectAwait(async o => new
					{
						o.Id,
						o.Name,
						OrganizationField = o.GetOrgField(OrganizationField),
						NonClosedTickets = await zendesk.GetTickets(o.Id)
							.Where(t => t.Status != "closed")
							.Where(t => t.CustomFields.Any(c => c.Id == CustomFieldId && c.Value?.ToString() == CustomFieldValue))
							.Select(t => new
							{
								t.Id,
								t.Priority,
								t.Subject,
								t.Status,
								t.CreatedAt,
								t.UpdatedAt,
								CustomFields = t.CustomFields.Where(c => c.Id == CustomFieldId),
							})
							.ToListAsync(),
					})
					.ToListAsync();

				Json.Out(foundOrganizations);
				logger.LogInformation("Found {Count} organizations.", foundOrganizations.Count);
			}
		}
	}
}
