using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace popilot.cli.Verbs
{
	[Verb("get-customers")]
	class GetCustomers
	{
		[Option(longName: "config", Required = true)]
		public string ConfigFile { get; set; } = null!;

		public async Task Do(IConfiguration config, ILogger<GetCustomers> logger, AzureDevOps azureDevOps, Zendesk zendesk, Productboard productboard)
		{
			var customers = JsonSerializer.Deserialize<Customers>(File.ReadAllText(ConfigFile), new JsonSerializerOptions()
			{
				ReadCommentHandling = JsonCommentHandling.Skip,
				AllowTrailingCommas = true,
			});
			var organizations = await Cached.Do<List<Zendesk.Organization>>("zendesk_organisations_cached.json", () => throw new NotImplementedException("Run get-organizations first."));

			var companies = await productboard.GetCompanies().ToListAsync();


			//Load report data
			var loadedCustomers = customers.Items
				.Select(customer => new
				{
					customerConfig = customer,
					organizations = organizations.Where(o => (customer.OrganizationFieldFilterValues ?? []).Any(f => o.GetOrgField(customers.OrganizationField)?.Contains(f, StringComparison.InvariantCultureIgnoreCase) ?? false))
				})
				.ToAsyncEnumerable()
				.SelectAwait(async customer => new
				{
					customer.customerConfig,

					tickets = await customer.organizations
						.ToAsyncEnumerable()
						.SelectAwait(async o => new
						{
							o.Id,
							o.Name,
							Tickets = await zendesk.GetTickets(o.Id)
								.Where(t => (customers.TicketStatusFilters ?? []).Any() ? (customers.TicketStatusFilters ?? []).Contains(t.Status) : true)
								.Where(t => t.CustomFields.Any(c => c.Id == customers.TicketCustomFieldId && c.Value?.ToString() == customers.TicketCustomFieldValue))
								.SelectAwait(async t => new
								{
									t.Id,
									t.Priority,
									t.Subject,
									t.Status,
									t.CreatedAt,
									t.UpdatedAt,
									CustomFields = t.CustomFields.Where(c => c.Id == customers.TicketCustomFieldId),
									Organization = new { o.Id, o.Name, OrganizationField = o.GetOrgField(customers.OrganizationField), Requestor = (await zendesk.GetUser(t.RequesterId))?.Email }
								})
								.ToListAsync(),
						})
						.SelectMany(o => o.Tickets.ToAsyncEnumerable())
						.OrderByDescending(o => o.CreatedAt)
						.ToListAsync(),

					workItems = customer.customerConfig.QueryId.HasValue
						? (await azureDevOps.GetBacklogWorkItems(customer.customerConfig.QueryId.Value, customers.QueryProject)).Roots
							.Where(t => (customers.WorkItemStatusFilterNegated, customers.WorkItemStatusFilter) switch
							{
								(_, null) => true,
								(false, _) => t.State == customers.WorkItemStatusFilter,
								(true, _) => t.State != customers.WorkItemStatusFilter
							})
						: null,

					notes = await companies
						.Where(c => c.Name.Contains(customer.customerConfig.Name, StringComparison.InvariantCultureIgnoreCase))
						.ToAsyncEnumerable()
						.SelectAwait(async c => new
						{
							info = c,
							notes = await productboard.GetNotes(c.Id)
								.Where(n => (customers.NotesTagFilters ?? []).Any() ? n.Tags.Intersect(customers.NotesTagFilters!, InvariantCultureIgnoreCaseComparer.Instance).Any() : true)
								.Select(n => new
								{
									note = n,
									company = c,
								})
								.ToListAsync()
						})
						.SelectMany(c => c.notes.ToAsyncEnumerable())
						.ToListAsync(),
				});


			//Export HTML report
			var html = new StringBuilder();
			html.AppendLine("<!html>");
			html.AppendLine("<body>");

			html.AppendLine($"""
				<span style="font-size: xx-small;">
					OrganizationField: {customers.OrganizationField}<br>
					TicketCustomFieldId: {customers.TicketCustomFieldId}<br>
					TicketCustomFieldValue: {customers.TicketCustomFieldValue}<br>
					TicketStatusFilters: {string.Join(',', customers.TicketStatusFilters ?? [])}<br>
					WorkItemStatusFilter: {customers.WorkItemStatusFilter}<br>
					WorkItemStatusFilterNegated: {customers.WorkItemStatusFilterNegated}<br>
					QueryProject: {customers.QueryProject}<br>
					NotesTagFilters: {string.Join(',', customers.NotesTagFilters ?? [])}<br>
					Generated: {DateTime.Now:F}
				</span>
				""");

			await foreach (var loadedCustomer in loadedCustomers)
			{
				Console.WriteLine(loadedCustomer.customerConfig.Name);
				Json.Out(loadedCustomer.tickets);
				Json.Out(loadedCustomer.workItems?.Select(wi => new
				{
					wi.Type,
					wi.Id,
					wi.Title,
					wi.State
				}));
				Json.Out(loadedCustomer.notes?.Select(n => new
				{
					company = new { n.company.Id, n.company.Name },
					n.note.Id,
					n.note.Title,
					n.note.DisplayUrl,
				}));

				html.AppendLine($"<h1>{loadedCustomer.customerConfig.Name}</h1>");

				if (loadedCustomer.tickets != null && loadedCustomer.tickets.Any())
				{
					html.AppendLine($"<h4>Tickets</h4>");
					var ticketLines = string.Join("<br>", (loadedCustomer.tickets ?? []).Select(t =>
					{
						string priority = t.Priority switch
						{
							"urgent" => $"""<span style="color:red;">{t.Priority}</span>""",
							"high" => $"""<span style="color:orange;">{t.Priority}</span>""",
							"normal" => $"""<span style="color:black;">{t.Priority}</span>""",
							"low" => $"""<span style="color:gray;">{t.Priority}</span>""",
							_ => t.Priority,
						};
						string status = t.Status switch
						{
							"solved" => $"""<span style="color:green;">{t.Status}</span>""",
							"closed" => $"""<span style="color:green;">{t.Status}</span>""",
							_ => t.Status,
						};

						string ticketLine = $"""
							<span>{Emoji.Known.Fire}<a href="https://{config["ZendeskSubdomain"]}.zendesk.com/agent/tickets/{t.Id}">{t.Id}</a></span>
							<span>{t.Subject}</span>
							<span style="color:gray;">[{priority}]</span>
							<span style="color:gray;">[{status}]</span>
							<span style="color:gray;">[{t.Organization.Requestor} {t.CreatedAt:d}]</span>
						""";

						return ticketLine;
					}));
					html.AppendLine($"{ticketLines}<br>");
				}

				if (loadedCustomer.workItems != null && loadedCustomer.workItems.Any())
				{
					html.AppendLine($"<h4>Work Items</h4>");
					var workItemLines = string.Join("<br>", (loadedCustomer.workItems ?? []).Select(w =>
					{
						string type = w.Type switch
						{
							"Epic" => Emoji.Known.Crown,
							"Feature" => Emoji.Known.GemStone,
							"Bug" => Emoji.Known.Collision,
							"User Story" => Emoji.Known.PersonInTuxedo,
							"Task" => Emoji.Known.CheckMarkButton,
							_ => w.Type,
						};
						string state = w.State switch
						{
							"Closed" => $"""<span style="color:green;">{w.State}</span>""",
							"Resolved" => $"""<span style="color:cyan;">{w.State}</span>""",
							"Active" => $"""<span style="color:yellow;">{w.State}</span>""",
							_ => $"""<span style="color:gray;">{w.State}</span>""",
						};

						return $"""
						<span>{type}<a href="{w.UrlHumanReadable()}">{w.Id}</a></span>
						<span>{w.Title}</span>
						<span style="color:gray;">[</span>{state}<span style="color:gray;">]</span>
						""";
					}));
					html.AppendLine($"{workItemLines}<br>");
				}

				if (loadedCustomer.notes != null && loadedCustomer.notes.Any())
				{
					html.AppendLine($"<h4>Notes</h4>");
					var noteLines = string.Join("<br>", (loadedCustomer.notes ?? []).Select(n =>
					{
						return $"""
						<span>{Emoji.Known.LightBulb}<a href="{n.note.DisplayUrl}">{n.note.Title}</a></span>
						<span style="color:gray;">[{n.note.State}]</span>
						<span style="color:gray;">[{n.note.CreatedBy?.Email} {n.note.CreatedAt:d}]</span>
						""";
					}));
					html.AppendLine($"{noteLines}<br>");
				}
			}
			html.AppendLine("</body>");

			string fileName = $"customers_{DateTime.Now:yyyyMMdd-HHmmss}.html";
			File.WriteAllText(fileName, html.ToString());
			Process.Start(new ProcessStartInfo(new FileInfo(fileName).FullName) { UseShellExecute = true });
		}

		private class InvariantCultureIgnoreCaseComparer : IEqualityComparer<string>
		{
			public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.InvariantCultureIgnoreCase);
			public int GetHashCode(string obj) => obj.GetHashCode();
			public static IEqualityComparer<string> Instance { get; } = new InvariantCultureIgnoreCaseComparer();
		}

		private class Customers
		{
			//Zendesk
			public string OrganizationField { get; set; } = null!;
			public long TicketCustomFieldId { get; set; }
			public string TicketCustomFieldValue { get; set; } = null!;
			public string[]? TicketStatusFilters { get; set; }

			//AzureDevOps
			public string? WorkItemStatusFilter { get; set; }
			public bool WorkItemStatusFilterNegated { get; set; }
			public string? QueryProject { get; set; }

			//Productboard
			public string[]? NotesTagFilters { get; set; }

			public Customer[] Items { get; set; } = null!;
			public class Customer
			{
				public string Name { get; set; } = null!;
				public string[]? OrganizationFieldFilterValues { get; set; }
				public Guid? QueryId { get; set; }
			}
		}
	}
}
