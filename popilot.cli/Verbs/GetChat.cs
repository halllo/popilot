using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Spectre.Console;

namespace popilot.cli.Verbs
{
	[Verb("get-chat")]
	class GetChat
	{
		[Value(0, MetaName = "name", Required = false)]
		public string Name { get; set; } = null!;

		[Option('t', longName: "take", Required = false)]
		public int? Take { get; set; }

		[Option('d', longName: "take-days", Required = false)]
		public int? TakeDays { get; set; }

		public async Task Do(GraphServiceClient graphClient, ILogger<GetChat> logger)
		{
			if (string.IsNullOrWhiteSpace(Name))
			{
				var chats = Take != null 
					? graphClient.GetMyChats().Take(Take.Value)
					: graphClient.GetMyChats();

				var chatCount = 0;
				await foreach (var chat in chats)
				{
					AnsiConsole.MarkupLine($"[gray]{chat.Id}[/] {Ansiable(chat.Topic)}");
					chatCount++;
				}
				logger.LogInformation($"Found {chatCount} chats");
			}
			else
			{
				var chat = await graphClient.GetMyChats(Name).FirstAsync();
				var messages = graphClient.GetChatMessages(chat.Id!);
				var messageCount = 0;
				await foreach (var message in messages.TakeWhile(m => !TakeDays.HasValue || m.CreatedDateTime > DateTime.UtcNow.AddDays(TakeDays.Value * -1)))
				{
					AnsiConsole.MarkupLine($"[gray]{message.CreatedDateTime} {message.Id} {message.From?.User?.DisplayName}[/] {Ansiable(message.Body?.Content)}");
					messageCount++;
				}
				logger.LogInformation($"Found {messageCount} messages");
			}
		}

		private static string Ansiable(string? name) => name?.Replace("[", "[[").Replace("]", "]]") ?? string.Empty;
	}
}