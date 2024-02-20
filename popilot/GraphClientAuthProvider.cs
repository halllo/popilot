using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;

namespace popilot
{
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

	file record TokenProvider(Func<Task<AuthenticationResult>> Auth) : IAccessTokenProvider
	{
		public AllowedHostsValidator AllowedHostsValidator => throw new NotImplementedException();
		public async Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
		{
			var result = await Auth();
			return result.AccessToken;
		}
	}
}
