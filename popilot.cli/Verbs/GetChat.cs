using CommandLine;
using Microsoft.Extensions.Logging;
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

		public async Task Do(Microsoft365 m365, ILogger<GetChat> logger)
		{
			if (string.IsNullOrWhiteSpace(Name))
			{
				var chats = Take != null 
					? m365.GetMyChats().Take(Take.Value)
					: m365.GetMyChats();

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
				var chat = await m365.GetMyChats(Name).FirstAsync();
				var messages = m365.GetChatMessages(chat.Id!);
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