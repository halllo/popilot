using Azure.AI.OpenAI;
using System.Text;

namespace popilot
{
	public static class OpenAiExtensions
	{
		public static async Task<string> GetMessageContentAsString(this Azure.Response<StreamingChatCompletions> response, bool consoleWrite = true)
		{
			var sb = new StringBuilder();
			using (StreamingChatCompletions streamingChatCompletions = response.Value)
			{
				await foreach (StreamingChatChoice choice in streamingChatCompletions.GetChoicesStreaming())
				{
					await foreach (ChatMessage message in choice.GetMessageStreaming())
					{
						sb.Append(message.Content);
						if (consoleWrite) Console.Write(message.Content);
					}
					sb.AppendLine();
					if (consoleWrite) Console.WriteLine();
				}
			}
			return sb.ToString();
		}
	}

	public interface IAi
	{
		OpenAIClient? Client { get; }
		string DeploymentOrModelName { get; }
	}

	public record OpenAiService(OpenAIClient? Client, string ModelName) : IAi
	{
		public string DeploymentOrModelName => ModelName;
	}

	public record AzureOpenAiService(OpenAIClient? Client, string DeploymentName) : IAi
	{
		public string DeploymentOrModelName => DeploymentName;
	}
}
