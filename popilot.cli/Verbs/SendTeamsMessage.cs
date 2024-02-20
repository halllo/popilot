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

		[Value(0, MetaName = "message", Required = true)]
		public string Message { get; set; } = null!;

		public async Task Do(GraphServiceClient graphClient, ILogger<SendTeamsMessage> logger)
		{
			var teams = await graphClient.Teams.GetAsync();
			var team = teams?.Value?.FirstOrDefault(t => string.Equals(t.DisplayName, Team, StringComparison.OrdinalIgnoreCase));
			if (team != null)
			{
				var channels = await graphClient.Teams[team.Id].Channels.GetAsync();
				var channel = channels?.Value?.FirstOrDefault(c => string.Equals(c.DisplayName, Channel, StringComparison.OrdinalIgnoreCase));
				if (channel != null)
				{
					var result = await graphClient.Teams[team.Id].Channels[channel.Id].Messages.PostAsync(new ChatMessage
					{
						Body = new ItemBody
						{
							Content = Message,
						},
					});
				}
				else
				{
					logger.LogError($"Channel '{Channel}' not found in team '{Team}'");
				}
			}
			else
			{
				logger.LogError($"Team '{Team}' not found");
			}
		}
	}
}