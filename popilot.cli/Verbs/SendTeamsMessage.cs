using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace popilot.cli.Verbs
{
	[Verb("send-teams-message")]
	class SendTeamsMessage
	{
		[Option('t', longName: "team", Required = true)]
		public string Team { get; set; } = null!;

		[Option('c', longName: "channel", Required = true)]
		public string Channel { get; set; } = null!;

		public async Task Do(GraphServiceClient graphClient, ILogger<SendTeamsMessage> logger)
		{
			var result = await graphClient.Teams[Team].Channels[Channel].Messages.PostAsync(new ChatMessage
			{
				Body = new ItemBody
				{
					Content = "Hello World",
				},
			});
		}
	}
}