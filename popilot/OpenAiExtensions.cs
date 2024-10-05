using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;

namespace popilot
{
	public static class OpenAiExtensions
	{
		public static async Task<string> GetMessageContentAsString(this AsyncCollectionResult<StreamingChatCompletionUpdate> response, bool consoleWrite = true)
		{
			var sb = new StringBuilder();
			await foreach (StreamingChatCompletionUpdate update in response)
			{
				foreach (ChatMessageContentPart updatePart in update.ContentUpdate)
				{
					sb.Append(updatePart.Text);
					if (consoleWrite) Console.Write(updatePart.Text);
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
