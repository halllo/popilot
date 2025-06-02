using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace popilot
{
	public class CustomerReport
	{
		private readonly AzureDevOps azureDevOps;
		private readonly Zendesk zendesk;
		private readonly Productboard productboard;
		private readonly Microsoft365 m365;
		private readonly IAi ai;
		private readonly ILogger logger;

		public CustomerReport(AzureDevOps azureDevOps, Zendesk zendesk, Productboard productboard, Microsoft365 m365, IAi ai, ILogger logger)
		{
			this.azureDevOps = azureDevOps;
			this.zendesk = zendesk;
			this.productboard = productboard;
			this.m365 = m365;
			this.ai = ai;
			this.logger = logger;
		}

		public class CustomersConfig
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

		public async Task<string> Generate(CustomersConfig config, List<Zendesk.Organization> zendeskOrganizations, string zendeskSubdomain, Action<Customer>? onCustomer = null)
		{
			var companies = await productboard.GetCompanies().ToListAsync();

			//Load report data
			var loadedCustomers = config.Items
				.Select(customer => new
				{
					customerConfig = customer,
					organizations = zendeskOrganizations.Where(o => (customer.OrganizationFieldFilterValues ?? []).Any(f => o.GetOrgField(config.OrganizationField)?.Contains(f, StringComparison.InvariantCultureIgnoreCase) ?? false))
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
								.Where(t => (config.TicketStatusFilters ?? []).Any() ? (config.TicketStatusFilters ?? []).Contains(t.Status) : true)
								.WhereIf(config.TicketCustomFieldFiltersOperator.Equals("OR", StringComparison.InvariantCultureIgnoreCase), t => config.TicketCustomFieldFilters.Any(f => t.CustomFields.Any(c => c.Id == f.Id && c.Value?.ToString() == f.Value)))
								.WhereIf(config.TicketCustomFieldFiltersOperator.Equals("AND", StringComparison.InvariantCultureIgnoreCase), t => config.TicketCustomFieldFilters.All(f => t.CustomFields.Any(c => c.Id == f.Id && c.Value?.ToString() == f.Value)))
								.SelectAwait(async t => new Customer.Ticket
								{
									Detailed = t,
									CustomFields = t.CustomFields.Where(c => c.Value != null),
									Organization = o,
									OrganizationField = o.GetOrgField(config.OrganizationField),
									Requestor = (await zendesk.GetUser(t.RequesterId))?.Email,
								})
								.ToListAsync(),
						})
						.SelectMany(o => o.Tickets.ToAsyncEnumerable())
						.OrderByDescending(o => o.Detailed.Status == "closed" || o.Detailed.Status == "solved" ? 0 : 1)
						.ThenByDescending(o => o.Detailed.Priority switch
						{
							"urgent" => 3,
							"high" => 2,
							"normal" => 1,
							"low" => 0,
							_ => -1,
						})
						.ThenByDescending(o => o.Detailed.CreatedAt)
						.ToListAsync(),

					workItems = customer.customerConfig.QueryId.HasValue
						? (await azureDevOps.GetBacklogWorkItems(customer.customerConfig.QueryId.Value, config.QueryProject)).Roots
							.Where(t => (config.WorkItemStatusFilterNegated, config.WorkItemStatusFilter) switch
							{
								(_, null) => true,
								(false, _) => t.State == config.WorkItemStatusFilter,
								(true, _) => t.State != config.WorkItemStatusFilter
							})
						: null,

					notes = await companies
						.Where(c => c.Name.Contains(customer.customerConfig.Name, StringComparison.InvariantCultureIgnoreCase))
						.ToAsyncEnumerable()
						.SelectAwait(async c => new
						{
							info = c,
							notes = await productboard.GetNotes(c.Id)
								.Where(n => (config.NotesTagFilters ?? []).Any() ? n.Tags.Intersect(config.NotesTagFilters!, InvariantCultureIgnoreCaseComparer.Instance).Any() : true)
								.Select(n => new Customer.Note
								{
									Detailed = n,
									Company = c,
								})
								.ToListAsync()
						})
						.SelectMany(c => c.notes.ToAsyncEnumerable())
						.ToListAsync(),
				})
				.Select(c => new Customer
				{
					Name = c.customerConfig.Name,
					Config = c.customerConfig,
					Tickets = c.tickets,
					WorkItems = c.workItems?.ToList() ?? [],
					Notes = c.notes,
				});

			//Generate HTML report
			var html = new StringBuilder();
			html.AppendLine("<!html>");
			html.AppendLine("<body>");

			html.AppendLine($"""
				<span style="font-size: xx-small;">
					OrganizationField: {config.OrganizationField}<br>
					TicketCustomFieldFiltersOperator: {config.TicketCustomFieldFiltersOperator}<br>
					TicketCustomFieldFilters: {JsonSerializer.Serialize(config.TicketCustomFieldFilters)}<br>
					TicketCustomFieldColumns: {string.Join(',', config.TicketCustomFieldColumns ?? [])}<br>
					TicketStatusFilters: {string.Join(',', config.TicketStatusFilters ?? [])}<br>
					WorkItemStatusFilter: {config.WorkItemStatusFilter}<br>
					WorkItemStatusFilterNegated: {config.WorkItemStatusFilterNegated}<br>
					QueryProject: {config.QueryProject}<br>
					NotesTagFilters: {string.Join(',', config.NotesTagFilters ?? [])}<br>
					Generated: {DateTime.Now:F}
				</span>
				""");

			await foreach (var loadedCustomer in loadedCustomers)
			{
				logger.LogInformation("Generating report for customer {Name}", loadedCustomer.Name);
				onCustomer?.Invoke(loadedCustomer);

				html.AppendLine($"<h1>{loadedCustomer.Name}</h1>");

				if (loadedCustomer.Tickets != null && loadedCustomer.Tickets.Any())
				{
					html.AppendLine($"<h4>Incidents</h4>");
					{
						var ticketLines = string.Join("<br>", loadedCustomer.Tickets.Select(t =>
						{
							var ticketId = $"<span>🔥<a href=\"https://{zendeskSubdomain}.zendesk.com/agent/tickets/{t.Detailed.Id} \"> {t.Detailed.Id}</a></span>";

							var ticketSubject = $"<span>{t.Detailed.Subject}</span>";

							var columns = (config.TicketCustomFieldColumns ?? [])
								.Select(c => t.CustomFields.FirstOrDefault(f => f.Id == c)?.Value)
								.Where(c => c is not null)
								.ToArray();
							var ticketColumns = columns.Any() ? $"<span style=\"color:gray;\">[{string.Join(" ", columns)}]</span>" : string.Empty;

							var priority = t.Detailed.Priority switch
							{
								"urgent" => $"""<span style="color:red;">{t.Detailed.Priority}</span>""",
								"high" => $"""<span style="color:orange;">{t.Detailed.Priority}</span>""",
								"normal" => $"""<span style="color:black;">{t.Detailed.Priority}</span>""",
								"low" => $"""<span style="color:gray;">{t.Detailed.Priority}</span>""",
								_ => t.Detailed.Priority,
							};
							var ticketPrio = $"<span style=\"color:gray;\">[{priority}]</span>";

							var status = t.Detailed.Status switch
							{
								"solved" => $"""<span style="color:green;">{t.Detailed.Status}</span>""",
								"closed" => $"""<span style="color:green;">{t.Detailed.Status}</span>""",
								_ => t.Detailed.Status,
							};
							var ticketStatus = $"<span style=\"color:gray;\">[status:{status}]</span>";

							var ticketProblemId = t.Detailed.ProblemId.HasValue ? $"<span><b>🔍<a href=\"https://{zendeskSubdomain}.zendesk.com/agent/tickets/{t.Detailed.ProblemId}\">{t.Detailed.ProblemId}</a></b></span>" : string.Empty;

							var ticketRequestor = $"<span style=\"color:gray;\">[{t.Requestor} {t.Detailed.CreatedAt:d}]</span>";

							return $"""
								<span {(t.Detailed.Status == "closed" || t.Detailed.Status == "solved" ? "style=\"text-decoration: line-through;\"" : string.Empty)}>
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
						var groupChatCatcher = new Regex("groupchat:(?<topic>.*)", RegexOptions.Compiled);

						html.AppendLine($"<table>");
						var headers =
							"""
							<th>problem</th>
							<th>incidents</th>
							<th>work items</th>
							<th>status</th>
							<th>next steps</th>
							<th>progress since yesterday</th>
							<th>progress since last week</th>
							""";
						html.AppendLine($"<tr>{headers}</tr>");

						var problemReferences = loadedCustomer.Tickets.GroupBy(t => t.Detailed.ProblemId).Where(p => p.Key.HasValue);
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
							var problemLabel = $"<span><b>🔍<a href=\"https://{zendeskSubdomain}.zendesk.com/agent/tickets/{problemId}\">{problemId}</a></b> {problem.Subject}</span>";

							var incidentLabels = problemReferences
								.FirstOrDefault(p => p.Key == problemId)
								?.Select(t => $"<span>🔥<a href=\"https://{zendeskSubdomain}.zendesk.com/agent/tickets/{t.Detailed.Id}\">{t.Detailed.Id}</a></span>")
								.ToList() ?? [];

							var columns = (config.TicketCustomFieldColumns ?? [])
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
								.Distinct(InvariantCultureIgnoreCaseComparer.Instance)
								.SelectAwait(async wid => await azureDevOps.GetWorkItems([int.Parse(wid)]))
								.SelectMany(ws => ws.ToAsyncEnumerable())
								.Where(w => w.State != "Removed")
								.OrderBy(w => w.State switch
								{
									"Closed" => 0,
									"Resolved" => 1,
									"Active" => 2,
									_ => 3,
								})
								.ThenBy(w => w.CreatedDate)
								.ToListAsync();

							var groupChats = await zendeskComments
								.SelectMany(c => groupChatCatcher.Matches(c.PlainBody).Select(m => m.Groups["topic"].Value))
								.Select(topic => topic.Replace("&amp;", "&").Trim())
								.ToAsyncEnumerable()
								.Distinct(InvariantCultureIgnoreCaseComparer.Instance)
								.SelectAwait(async topic =>
								{
									try
									{
										var chat = await m365.GetMyChats(topic).FirstAsync();
										return new
										{
											chat,
											lastSevenDayMessages = await m365
												.GetChatMessages(chat.Id!)
												.TakeWhile(m => m.CreatedDateTime > DateTime.UtcNow.AddDays(-7))
												.OrderBy(m => m.CreatedDateTime)
												.ToListAsync()
										};
									}
									catch (Exception e)
									{
										logger.LogError(e, $"Error loading chat {topic}");
										return null;
									}
								})
								.Where(c => c != null)
								.Select(c => c!)
								.ToListAsync();

							Dictionary<int, Microsoft.TeamFoundation.Work.WebApi.TeamSettingsIteration?> workItemIdToIteration = [];
							foreach (var wi in workItems)
							{
								var path = wi.IterationPath.StartsWith(wi.TeamProject) ? wi.IterationPath.Substring(wi.TeamProject.Length + 1) : wi.IterationPath;
								var iterations = await azureDevOps.GetAllIterations(wi.TeamProject, null, path);
								var iteration = iterations.FirstOrDefault(i => i.Path == wi.IterationPath);
								workItemIdToIteration.Add(wi.Id, iteration);
							}
							string? plannedFinishDate(AzureDevOps.IWorkItemDto w)
							{
								var targetDate = w.TargetDate ?? (workItemIdToIteration.ContainsKey(w.Id) ? workItemIdToIteration[w.Id]?.Attributes.FinishDate : null);
								return targetDate.HasValue ? $"""<span style="color:gray; font-size:small;">planned for {targetDate:dd.MM.yyyy}</span>""" : "";
							}

							var workItemLabels = workItems
								.Select(w =>
								{
									string type = w.Type switch
									{
										"Epic" => "👑",
										"Feature" => "💎",
										"Bug" => "💥",
										"User Story" => "🗣️",
										"Task" => "📋",
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
											<span>{type}<a href="{w.UrlHumanReadable()}" title="{w.Title}">{w.Id}</a></span>
											<span style="color:gray;">[</span>{state}<span style="color:gray;">]</span>
											{plannedFinishDate(w)}
											""";
								})
								.ToList();

							var overallState =
								workItems.All(w => w.State == "Closed") && (problem.Status == "closed" || problem.Status == "solved") ? "Solved" :
								workItems.Any() && workItems.All(w => w.State == "Closed") ? "Solution Delivered" :
								workItems.Any(w => w.State == "Resolved" || w.State == "Active") ? "Work in Progress" :
								"Open";

							var nextSteps = workItems
								.Where(w => w.State != "Closed")
								.Select(w => w.State switch
								{
									"Resolved" => $"QA/testing {w.Title} {plannedFinishDate(w)}",
									"Active" => $"Completing {w.Title} {plannedFinishDate(w)}",
									_ => $"{w.Title} {plannedFinishDate(w)}",
								})
								.Select(l => Regex.Replace(l, "\\sZD\\d+", string.Empty).Trim());

							var azuredevopsComments = await workItems
								.ToAsyncEnumerable()
								.SelectAwait(async w => await azureDevOps.GetComments(w.Id))
								.SelectMany(c => c.ToAsyncEnumerable())
								.OrderBy(c => c.RevisedDate)
								.ToListAsync();

							var comments = Enumerable
								.Concat(
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
								)
								.Concat
								(
									groupChats.SelectMany(c => c.lastSevenDayMessages.Select(m => new
									{
										Date = m.CreatedDateTime ?? DateTimeOffset.MinValue,
										Text = m.Body?.Content ?? string.Empty,
									}))
								)
								.OrderBy(c => c.Date)
								.Where(c => !Uri.IsWellFormedUriString(c.Text, UriKind.Absolute))//exclude lonely URLs
								.ToList();

							const string processSummaryPrompt = "Summarize the progress described in these comments of a support ticket. Be as short and concise as possible. The shorter the better.";

							var commentsSinceYesterday = comments.Where(c => c.Date > DateTimeOffset.UtcNow.AddDays(-1));
							var progressSinceYesterday = commentsSinceYesterday.Any()
								? await ai.Ask(
									prompt: processSummaryPrompt + " Start with 'Since yesterday...'",
									content: string.Join("\n\n", commentsSinceYesterday.Select(c => c.Text)))
								: "No progress since yesterday.";

							var commentsSinceLastWeek = comments.Where(c => c.Date > DateTimeOffset.UtcNow.AddDays(-7));
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
								{td(strikethroughIfClosed($"{overallState} {string.Join("", groupChats.Select(c => $"""<span title="{c.chat.Topic}">🗨</span>"""))}".Trim()))}
								{td(strikethroughIfClosed(string.Join("; ", nextSteps)))}
								{td(strikethroughIfClosed(progressSinceYesterday))}
								{td(strikethroughIfClosed(progressSinceLastWeek))}
								""";

							html.AppendLine($"<tr>{row}</tr>");
						}
						html.AppendLine($"</table>");
					}
				}

				if (loadedCustomer.WorkItems != null && loadedCustomer.WorkItems.Any())
				{
					html.AppendLine($"<h4>Work Items</h4>");
					var workItemLines = string.Join("<br>", (loadedCustomer.WorkItems ?? []).Select(w =>
					{
						string type = w.Type switch
						{
							"Epic" => "👑",
							"Feature" => "💎",
							"Bug" => "💥",
							"User Story" => "🗣️",
							"Task" => "📋",
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

				if (loadedCustomer.Notes != null && loadedCustomer.Notes.Any())
				{
					html.AppendLine($"<h4>Notes</h4>");
					var noteLines = string.Join("<br>", (loadedCustomer.Notes ?? []).Select(n =>
					{
						return $"""
						<span>💡<a href="{n.Detailed.DisplayUrl}">{n.Detailed.Title}</a></span>
						<span style="color:gray;">[{n.Detailed.State}]</span>
						<span style="color:gray;">[{n.Detailed.CreatedBy?.Email} {n.Detailed.CreatedAt:d}]</span>
						""";
					}));
					html.AppendLine($"{noteLines}<br>");
				}
			}

			html.AppendLine("</body>");

			return html.ToString();
		}

		public class Customer
		{
			public string Name { get; internal set; } = null!;
			public CustomersConfig.Customer Config { get; internal set; } = null!;
			public IReadOnlyList<AzureDevOps.IWorkItemDto> WorkItems { get; internal set; } = null!;
			public IReadOnlyList<Ticket> Tickets { get; internal set; } = null!;
			public IReadOnlyList<Note> Notes { get; internal set; } = null!;

			public class Ticket
			{
				public Zendesk.Ticket Detailed { get; internal set; } = null!;
				public IEnumerable<Zendesk.CustomField> CustomFields { get; internal set; } = null!;
				public Zendesk.Organization Organization { get; internal set; } = null!;
				public string? OrganizationField { get; internal set; }
				public string? Requestor { get; internal set; }
			}

			public class Note
			{
				public Productboard.Note Detailed { get; internal set; } = null!;
				public Productboard.Company Company { get; internal set; } = null!;
			}
		}
	}
}
