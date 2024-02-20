using Azure.AI.OpenAI;
using Spectre.Console;
using static ColoredConsole;

namespace popilot
{
    public static class Summarizer
    {
        public static string ESCALATION = $@"
Du bist ein guter Product Owner. Fasse die Probleme zusammen. Dazu erhältst du einen Snapshot der Themen als JSON, der die Work Items enthält. Zusammenhängende Work Items haben das selbe Topic. Verwende die Titel der Work Items auf keinen Fall wortwörtlich, sondern achte darauf, dass die Zusammenfassung leicht und schnell lesbar ist. Erwähne alle Topics in kurzen Sätzen. Verwende keine Sätze mit Doppelpunkten.
Fasse dich kurz. Abkürzungen und Akronyme sollen nicht ausgeschrieben werden. Beginne mit ""In dieser Eskalation geht es um ...""
";
        public static string SPRINT = $@"
Du bist ein guter Product Owner. Fasse die Lösungen zusammen, die das Team in diesem Sprint bearbeitet oder schon gelöst hat. Dazu erhältst du einen Snapshot der Themen als JSON, der die Work Items enthält. Zusammenhängende Work Items haben das selbe Topic. Verwende die Titel der Work Items auf keinen Fall wortwörtlich, sondern achte darauf, dass die Zusammenfassung leicht und schnell lesbar ist. Erwähne alle Topics in kurzen Sätzen. Verwende keine Sätze mit Doppelpunkten.
Fasse dich kurz. Abkürzungen und Akronyme sollen nicht ausgeschrieben werden. Beginne mit ""In diesem Sprint ..."".
";

        public static Task<string> Summarize(this OpenAiService openai, IEnumerable<AzureDevOps.IWorkItemDto> workItems, string prompt, bool consoleWrite = true)
		{
            var json = Json(workItems.Select(w => new
            {
                w.State,
                w.Title,
                Topic = w.ParentTitle,
            }), indented: false);
            return openai.Summarize(json, prompt, consoleWrite);
        }

        public static async Task<string> Summarize(this OpenAiService openai, string content, string prompt, bool consoleWrite = true)
		{
			if (openai.Client == null) throw new NotSupportedException("Summaries without OpenAI config are not supported. Please add an OpenAiApiKey or use the --no-ai option!");
            
			var response = await openai.Client.GetChatCompletionsStreamingAsync(
                deploymentOrModelName: "gpt-4-1106-preview",
                chatCompletionsOptions: new ChatCompletionsOptions()
                {
                    Temperature = 0.0f,
                    Messages =
                    {
                        new ChatMessage(ChatRole.System, prompt),
                        new ChatMessage(ChatRole.User, content),
                    }
                });
            return await response.GetMessageContentAsString(consoleWrite);
        }
    }
}
