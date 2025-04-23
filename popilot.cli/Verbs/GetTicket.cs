using CommandLine;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace popilot.cli.Verbs
{
	[Verb("get-ticket")]
	class GetTicket
	{
		[Value(0, MetaName = "ticket ID", Required = true)]
		public int TicketId { get; set; }

		[Option(longName: "all-values", HelpText = "by default all custom values with null value are hidden")]
		public bool ShowAllValues { get; set; }

		[Option('c', longName: "comments", HelpText = "show comments")]
		public bool ShowComments { get; set; }

		public async Task Do(ILogger<GetTicket> logger, Zendesk zendesk)
		{
			if (ShowAllValues)
			{
				var ticket = await zendesk.GetTicketRaw(TicketId);
				Json.Out(ticket);
			}
			else
			{
				var ticket = await zendesk.GetTicket(TicketId);
				var json = JsonSerializer.Serialize(ticket, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
				var replacedJson = Regex.Replace(json, "\\{[^}]*?\\\"value\\\":\\s?null\\s*?\\},?", m => string.Empty, RegexOptions.Singleline);
				var filteredTicket = JsonSerializer.Deserialize<Zendesk.Ticket>(replacedJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, AllowTrailingCommas = true });
				Json.Out(filteredTicket);
			}

			if (ShowComments)
			{
				if (ShowAllValues)
				{
					var json = await zendesk.GetTicketCommentsRaw(TicketId);
					Json.Out(json);
				}
				else
				{
					await foreach (var comment in zendesk.GetTicketComments(TicketId))
					{
						Json.Out(new { comment.Id, comment.AuthorId, comment.CreatedAt, comment.PlainBody });
					}
				}
			}
		}
	}
}
