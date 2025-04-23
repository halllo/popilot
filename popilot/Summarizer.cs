using OpenAI.Chat;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace popilot
{
	public static class Summarizer
	{
		public static string ESCALATION = $@"
Du bist ein guter Product Owner. Fasse die Probleme zusammen. Dazu erhältst du einen Snapshot der Themen als JSON, der die Work Items enthält. Zusammenhängende Work Items haben das selbe Topic. Verwende die Titel der Work Items auf keinen Fall wortwörtlich, sondern achte darauf, dass die Zusammenfassung leicht und schnell lesbar ist. Erwähne alle Topics in kurzen Sätzen. Verwende keine Sätze mit Doppelpunkten.
Fasse dich kurz. Erwähne Work Items im Status 'New' als geplant und Work Items im Status 'Active' als aktuell in der Umsetzung und behaupte nicht, dass sie schon implementiert worden wären. Abkürzungen und Akronyme sollen nicht ausgeschrieben werden. Beginne mit ""In dieser Eskalation geht es um ...""
";
		public static string SPRINT = $@"
Du bist ein guter Product Owner. Fasse die Lösungen zusammen, die das Team in diesem Sprint bearbeitet oder schon gelöst hat. Dazu erhältst du einen Snapshot der Themen als JSON, der die Work Items enthält. Zusammenhängende Work Items haben das selbe Topic. Verwende die Titel der Work Items nicht wortwörtlich, sondern achte darauf, dass die Zusammenfassung leicht und schnell lesbar ist. Erwähne alle Topics in kurzen Sätzen. Verwende keine Sätze mit Doppelpunkten.
Fasse dich kurz. Erwähne Work Items im Status 'New' als geplant und Work Items im Status 'Active' als aktuell in der Umsetzung und behaupte nicht, dass sie schon implementiert worden wären. Abkürzungen und Akronyme sollen nicht ausgeschrieben werden. Beginne mit ""In diesem Sprint haben wir ..."".
";

		public static Task<string> Summarize(this IAi ai, IEnumerable<AzureDevOps.IWorkItemDto> workItems, string prompt, bool consoleWrite = true)
		{
			var json = Json(workItems.Select(w => new
			{
				w.State,
				w.Reason,
				w.Title,
				Topic = w.ParentTitle,
			}), indented: false);
			return ai.Ask(prompt, json, consoleWrite);
		}

		private static string Json(object? o, bool indented = true) => o == null ? "<null>" : JsonSerializer.Serialize(o, o.GetType(), new JsonSerializerOptions
		{
			WriteIndented = indented,
			Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
		});

		public static async Task<string> Ask(this IAi ai, string prompt, string content, bool consoleWrite = true)
		{
			if (ai.Client == null) throw new NotSupportedException("Summaries without OpenAI config are not supported. Please add an OpenAiApiKey or use the --no-ai option!");

			var chatClient = ai.Client.GetChatClient(ai.DeploymentOrModelName);
			var response = chatClient.CompleteChatStreamingAsync(
				messages: [
					ChatMessage.CreateSystemMessage(prompt),
					ChatMessage.CreateUserMessage(content)
				],
				options: new ChatCompletionOptions
				{
					Temperature = 0.0f
				});

			return await response.GetMessageContentAsString(consoleWrite);
		}
	}
}
