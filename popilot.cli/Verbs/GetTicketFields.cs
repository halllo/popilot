using CommandLine;
using Microsoft.Extensions.Logging;

namespace popilot.cli.Verbs
{
	[Verb("get-ticket-fields")]
	class GetTicketFields
	{
		public async Task Do(ILogger<GetTicketFields> logger, Zendesk zendesk)
		{
			var ticketFields = await zendesk.GetTicketFields();
			Json.Out(ticketFields);
		}
	}
}
