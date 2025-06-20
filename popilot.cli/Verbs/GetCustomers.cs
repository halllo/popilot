using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace popilot.cli.Verbs
{
	[Verb("get-customers")]
	class GetCustomers
	{
		[Option(longName: "config", Required = true)]
		public string ConfigFile { get; set; } = null!;

		public async Task Do(IConfiguration config, ILogger<GetCustomers> logger, AzureDevOps azureDevOps, Zendesk zendesk, Productboard productboard, Microsoft365 m365, IAi ai)
		{
			var customers = JsonSerializer.Deserialize<CustomerReport.CustomersConfig>(File.ReadAllText(ConfigFile), new JsonSerializerOptions()
			{
				ReadCommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true,
			});
			var organizations = await Cached.Do<List<Zendesk.Organization>>("zendesk_organisations_cached.json", () => throw new NotImplementedException("Run get-organizations first."));

			var customerReport = new CustomerReport(azureDevOps, zendesk, productboard, m365, ai, logger);
			var html = await customerReport.Generate(
				config: customers!,
				zendeskOrganizations: organizations,
				zendeskSubdomain: config["ZendeskSubdomain"]!,
				onCustomer: customer =>
				{
					Console.WriteLine(customer.Name);
					Json.Out(customer.Tickets.Select(t => new
					{
						t.Detailed.Id,
						t.Detailed.Subject,
						t.Detailed.Status,
					}));
					Json.Out(customer.WorkItems?.Select(wi => new
					{
						wi.Type,
						wi.Id,
						wi.Title,
						wi.State
					}));
					Json.Out(customer.Notes?.Select(n => new
					{
						company = new { n.Company.Id, n.Company.Name },
						n.Detailed.Id,
						n.Detailed.Title,
						n.Detailed.DisplayUrl,
					}));
				});

			string fileName = $"customers_{DateTime.Now:yyyyMMdd-HHmmss}.html";
			File.WriteAllText(fileName, html);
			Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
		}
	}
}
