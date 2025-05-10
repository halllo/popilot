using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace popilot
{
	public static class GraphClientExtensions
	{
		public static async IAsyncEnumerable<Chat> GetMyChats(this GraphServiceClient graph, string? topicFilter = null)
		{
			ChatCollectionResponse? chatCollection = null;
			do
			{
				chatCollection = chatCollection != null && chatCollection.OdataNextLink != null
					? await graph.Me.Chats.WithUrl(chatCollection.OdataNextLink).GetAsync()
					: await graph.Me.Chats.GetAsync(string.IsNullOrWhiteSpace(topicFilter) ? null : c => c.QueryParameters.Filter = $"topic eq '{topicFilter}'");

				foreach (var chat in chatCollection?.Value ?? [])
				{
					yield return chat;
				}

			} while (chatCollection?.OdataNextLink != null);
		}

		public static async IAsyncEnumerable<ChatMessage> GetChatMessages(this GraphServiceClient graph, string id)
		{
			ChatMessageCollectionResponse? messageCollection = null;
			do
			{
				messageCollection = messageCollection != null && messageCollection.OdataNextLink != null
					? await graph.Me.Chats[id].Messages.WithUrl(messageCollection.OdataNextLink).GetAsync()
					: await graph.Me.Chats[id].Messages.GetAsync();

				foreach (var message in messageCollection?.Value ?? [])
				{
					yield return message;
				}

			} while (messageCollection?.OdataNextLink != null);
		}
	}
}
