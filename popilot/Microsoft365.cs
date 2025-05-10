using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Identity.Client;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions;

namespace popilot
{
	public class Microsoft365
	{
		public GraphServiceClient Graph { get; init; }

		public Microsoft365(GraphServiceClient graph)
		{
			this.Graph = graph;
		}

		public async IAsyncEnumerable<Chat> GetMyChats(string? topicFilter = null)
		{
			ChatCollectionResponse? chatCollection = null;
			do
			{
				chatCollection = chatCollection != null && chatCollection.OdataNextLink != null
					? await Graph.Me.Chats.WithUrl(chatCollection.OdataNextLink).GetAsync()
					: await Graph.Me.Chats.GetAsync(string.IsNullOrWhiteSpace(topicFilter) ? null : c => c.QueryParameters.Filter = $"topic eq '{topicFilter}'");

				foreach (var chat in chatCollection?.Value ?? [])
				{
					yield return chat;
				}

			} while (chatCollection?.OdataNextLink != null);
		}

		public async IAsyncEnumerable<ChatMessage> GetChatMessages(string id)
		{
			ChatMessageCollectionResponse? messageCollection = null;
			do
			{
				messageCollection = messageCollection != null && messageCollection.OdataNextLink != null
					? await Graph.Me.Chats[id].Messages.WithUrl(messageCollection.OdataNextLink).GetAsync()
					: await Graph.Me.Chats[id].Messages.GetAsync();

				foreach (var message in messageCollection?.Value ?? [])
				{
					yield return message;
				}

			} while (messageCollection?.OdataNextLink != null);
		}

		public class GraphClientAuthProvider : IAuthenticationProvider
		{
			private readonly ILogger<GraphClientAuthProvider> logger;
			private readonly IPublicClientApplication authClient;
			private readonly BaseBearerTokenAuthenticationProvider bearerAuthProvider;

			public GraphClientAuthProvider(IConfiguration config, ILogger<GraphClientAuthProvider> logger)
			{
				this.logger = logger;

				string clientId = config["GraphClientId"]!;
				string? tenantId = config["MicrosoftTenantId"];
				string[] scopes = ["https://graph.microsoft.com/.default"];
				this.authClient = PublicClientApplicationBuilder.Create(clientId)
					.WithTenantIdIfNotNullNorEmpty(tenantId)
					.WithDefaultRedirectUri()
					.Build();

				MsalCacheHelper? cacheHelper = default;
				this.bearerAuthProvider = new BaseBearerTokenAuthenticationProvider(new TokenProvider(async () =>
				{
					if (cacheHelper == null)
					{
						var storageProperties = new StorageCreationPropertiesBuilder("msal.cache"/*collisions with azure devops?*/, ".").Build();
						var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
						cacheHelper.RegisterCache(authClient.UserTokenCache);
					}

					try
					{
						var accounts = await authClient.GetAccountsAsync();
						logger.LogInformation("Attempting silent login: {Accounts}", accounts);
						var result = await authClient.AcquireTokenSilent(scopes, accounts.FirstOrDefault()).ExecuteAsync();
						return result;
					}
					catch (Exception)
					{
						logger.LogInformation("Interactive login required");
						var result = await authClient.AcquireTokenInteractive(scopes).ExecuteAsync();
						return result;
					}
				}));
			}

			public Task AuthenticateRequestAsync(RequestInformation request, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
			{
				return this.bearerAuthProvider.AuthenticateRequestAsync(request, additionalAuthenticationContext, cancellationToken);
			}
		}

		private record TokenProvider(Func<Task<AuthenticationResult>> Auth) : IAccessTokenProvider
		{
			public AllowedHostsValidator AllowedHostsValidator => throw new NotImplementedException();
			public async Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
			{
				var result = await Auth();
				return result.AccessToken;
			}
		}
	}
}
