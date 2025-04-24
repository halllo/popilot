using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using popilot;
using Spectre.Console;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace popilot.cli.Verbs
{
	[Verb("get-customers")]
	class GetCustomers
	{
		[Option(longName: "config", Required = true)]
		public string ConfigFile { get; set; } = null!;

		public async Task Do(IConfiguration config, ILogger<GetCustomers> logger, AzureDevOps azureDevOps, Zendesk zendesk, Productboard productboard, IAi ai)
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
								.WhereIf(customers.TicketCustomFieldFiltersOperator.Equals("OR", StringComparison.InvariantCultureIgnoreCase), t => customers.TicketCustomFieldFilters.Any(f => t.CustomFields.Any(c => c.Id == f.Id && c.Value?.ToString() == f.Value)))
								.WhereIf(customers.TicketCustomFieldFiltersOperator.Equals("AND", StringComparison.InvariantCultureIgnoreCase), t => customers.TicketCustomFieldFilters.All(f => t.CustomFields.Any(c => c.Id == f.Id && c.Value?.ToString() == f.Value)))
								.SelectAwait(async t => new
								{
									t.Id,
									t.Priority,
									t.Subject,
									t.Status,
									t.CreatedAt,
									t.UpdatedAt,
									CustomFields = t.CustomFields.Where(c => c.Value != null),
									Organization = new { o.Id, o.Name, OrganizationField = o.GetOrgField(customers.OrganizationField), Requestor = (await zendesk.GetUser(t.RequesterId))?.Email },
									t.ProblemId,
								})
								.ToListAsync(),
						})
						.SelectMany(o => o.Tickets.ToAsyncEnumerable())
						.OrderByDescending(o => o.Status == "closed" || o.Status == "solved" ? 0 : 1)
						.ThenByDescending(o => o.Priority switch
						{
							"urgent" => 3,
							"high" => 2,
							"normal" => 1,
							"low" => 0,
							_ => -1,
						})
						.ThenByDescending(o => o.CreatedAt)
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
					TicketCustomFieldFiltersOperator: {customers.TicketCustomFieldFiltersOperator}<br>
					TicketCustomFieldFilters: {JsonSerializer.Serialize(customers.TicketCustomFieldFilters)}<br>
					TicketCustomFieldColumns: {string.Join(',', customers.TicketCustomFieldColumns ?? [])}<br>
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
					html.AppendLine($"<h4>Incidents</h4>");
					{
						var ticketLines = string.Join("<br>", loadedCustomer.tickets.Select(t =>
						{
							var ticketId = $"<span>{Emoji.Known.Fire}<a href=\"https://{config["ZendeskSubdomain"]}.zendesk.com/agent/tickets/{t.Id}\">{t.Id}</a></span>";

							var ticketSubject = $"<span>{t.Subject}</span>";

							var columns = (customers.TicketCustomFieldColumns ?? [])
								.Select(c => t.CustomFields.FirstOrDefault(f => f.Id == c)?.Value)
								.Where(c => c is not null)
								.ToArray();
							var ticketColumns = columns.Any() ? $"<span style=\"color:gray;\">[{string.Join(" ", columns)}]</span>" : string.Empty;

							var priority = t.Priority switch
							{
								"urgent" => $"""<span style="color:red;">{t.Priority}</span>""",
								"high" => $"""<span style="color:orange;">{t.Priority}</span>""",
								"normal" => $"""<span style="color:black;">{t.Priority}</span>""",
								"low" => $"""<span style="color:gray;">{t.Priority}</span>""",
								_ => t.Priority,
							};
							var ticketPrio = $"<span style=\"color:gray;\">[{priority}]</span>";

							var status = t.Status switch
							{
								"solved" => $"""<span style="color:green;">{t.Status}</span>""",
								"closed" => $"""<span style="color:green;">{t.Status}</span>""",
								_ => t.Status,
							};
							var ticketStatus = $"<span style=\"color:gray;\">[status:{status}]</span>";

							var ticketProblemId = t.ProblemId.HasValue ? $"<span><b>{Emoji.Known.Detective}<a href=\"https://{config["ZendeskSubdomain"]}.zendesk.com/agent/tickets/{t.ProblemId}\">{t.ProblemId}</a></b></span>" : string.Empty;

							var ticketRequestor = $"<span style=\"color:gray;\">[{t.Organization.Requestor} {t.CreatedAt:d}]</span>";

							return $"""
								<span {(t.Status == "closed" || t.Status == "solved" ? "style=\"text-decoration: line-through;\"" : string.Empty)}>
								{ticketId}
								{ticketSubject}
								{ticketColumns}
								{ticketPrio}
								{ticketStatus}
								{ticketProblemId}
								{ticketRequestor}
								</span>
								""";
						}));
						html.AppendLine($"{ticketLines}<br>");
					}

					html.AppendLine($"<h4>Problems</h4>");
					{
						var workItemCatcher = new Regex("https\\:\\/\\/dev\\.azure\\.com.*?/edit/(?<wid>\\d*?)(\\s|$)", RegexOptions.Singleline | RegexOptions.Compiled);

						html.AppendLine($"<table>");
						var headers =
							"""
							<th>problem</th>
							<th>incidents</th>
							<th>work items</th>
							<th>progress since yesterday</th>
							<th>progress since last week</th>
							""";
						html.AppendLine($"<tr>{headers}</tr>");

						var problemReferences = loadedCustomer.tickets.GroupBy(t => t.ProblemId).Where(p => p.Key.HasValue);
						var problems = problemReferences
							.ToAsyncEnumerable()
							.SelectAwait(async p => await zendesk.GetTicket(p.Key!.Value))
							.Where(p => p != null)
							.Select(p => p!)
							.OrderByDescending(p => p.Status == "closed" || p.Status == "solved" ? 0 : 1)
							.ThenByDescending(p => p.Priority switch
							{
								"urgent" => 3,
								"high" => 2,
								"normal" => 1,
								"low" => 0,
								_ => -1,
							})
							.ThenByDescending(p => p.CreatedAt);

						await foreach (var problem in problems)
						{
							var problemId = (int)problem.Id;
							var problemLabel = $"<span><b>{Emoji.Known.Detective}<a href=\"https://{config["ZendeskSubdomain"]}.zendesk.com/agent/tickets/{problemId}\">{problemId}</a></b> {problem.Subject}</span>";

							var incidentLabels = problemReferences
								.FirstOrDefault(p => p.Key == problemId)
								?.Select(t => $"<span>{Emoji.Known.Fire}<a href=\"https://{config["ZendeskSubdomain"]}.zendesk.com/agent/tickets/{t.Id}\">{t.Id}</a></span>")
								.ToList() ?? [];

							var columns = (customers.TicketCustomFieldColumns ?? [])
								.Select(c => problem.CustomFields.FirstOrDefault(f => f.Id == c)?.Value)
								.Where(c => c is not null)
								.ToArray();
							var problemColumns = columns.Any() ? $"<span style=\"color:gray;\">[{string.Join(" ", columns)}]</span>" : string.Empty;

							var priority = problem.Priority switch
							{
								"urgent" => $"""<span style="color:red;">{problem.Priority}</span>""",
								"high" => $"""<span style="color:orange;">{problem.Priority}</span>""",
								"normal" => $"""<span style="color:black;">{problem.Priority}</span>""",
								"low" => $"""<span style="color:gray;">{problem.Priority}</span>""",
								_ => problem.Priority,
							};
							var problemPriority = $"<span style=\"color:gray;\">[{priority}]</span>";

							var status = problem.Status switch
							{
								"solved" => $"""<span style="color:green;">{problem.Status}</span>""",
								"closed" => $"""<span style="color:green;">{problem.Status}</span>""",
								_ => problem.Status,
							};
							var problemStatus = $"<span style=\"color:gray;\">[{status}]</span>";

							var zendeskComments = await zendesk.GetTicketComments(problemId)
								.OrderBy(c => c.CreatedAt)
								.Skip(1)//we ignore the initial comment because it is the problem description
								.ToListAsync();

							var workItems = await zendeskComments
								.SelectMany(c => workItemCatcher.Matches(c.PlainBody).Select(m => m.Groups["wid"].Value))
								.ToAsyncEnumerable()
								.SelectAwait(async wid => await azureDevOps.GetWorkItems([int.Parse(wid)]))
								.SelectMany(ws => ws.ToAsyncEnumerable())
								.ToListAsync();

							var workItemLabels = workItems
								.Select(w =>
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
											<span style="color:gray;">[</span>{state}<span style="color:gray;">]</span>
											""";
								})
								.ToList();

							var azuredevopsComments = await workItems
								.ToAsyncEnumerable()
								.SelectAwait(async w => await azureDevOps.GetComments(w.Id))
								.SelectMany(c => c.ToAsyncEnumerable())
								.OrderBy(c => c.RevisedDate)
								.ToListAsync();

							var comments = Enumerable.Concat(
								zendeskComments.Select(c => new
								{
									Date = c.CreatedAt,
									Text = c.PlainBody,
								}),
								azuredevopsComments.Select(c => new
								{
									Date = new DateTimeOffset(c.RevisedDate),
									Text = c.Text,
								})
							);

							const string processSummaryPrompt = "Summarize the progress described in these comments of a support ticket. Be as short and concise as possible. The shorter the better.";

							var commentsSinceYesterday = comments.Where(c => c.Date > DateTimeOffset.Now.AddDays(-1));
							var progressSinceYesterday = commentsSinceYesterday.Any()
								? await ai.Ask(
									prompt: processSummaryPrompt + " Start with 'Since yesterday...'",
									content: string.Join("\n\n", commentsSinceYesterday.Select(c => c.Text)))
								: "No progress since yesterday.";

							var commentsSinceLastWeek = comments.Where(c => c.Date > DateTimeOffset.Now.AddDays(-7));
							var progressSinceLastWeek = commentsSinceLastWeek.Any()
								? await ai.Ask(
									prompt: processSummaryPrompt + " Start with 'Since last week...'",
									content: string.Join("\n\n", commentsSinceLastWeek.Select(c => c.Text)))
								: "No progress since last week.";

							var closed = problem.Status == "closed" || problem.Status == "solved";
							string td(string s) => $"<td>{s}</td>";
							string strikethroughIfClosed(string? s) => closed ? $"<span style=\"text-decoration: line-through;\">{s}</span>" : (s ?? "");
							var row =
								$"""
								{td(strikethroughIfClosed($"{problemLabel} {problemPriority} {problemStatus}"))}
								{td(strikethroughIfClosed(string.Join(" ", incidentLabels)))}
								{td(strikethroughIfClosed(string.Join(" ", workItemLabels)))}
								{td(strikethroughIfClosed(progressSinceYesterday))}
								{td(strikethroughIfClosed(progressSinceLastWeek))}
								""";

							html.AppendLine($"<tr>{row}</tr>");
						}
						html.AppendLine($"</table>");
					}
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
			public string TicketCustomFieldFiltersOperator { get; set; } = null!;
			public FieldFilter[] TicketCustomFieldFilters { get; set; } = null!;
			public class FieldFilter
			{
				public long Id { get; set; }
				public string Value { get; set; } = null!;
			}
			public string[]? TicketStatusFilters { get; set; }
			public long[]? TicketCustomFieldColumns { get; set; }

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
