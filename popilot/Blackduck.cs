using Microsoft.Extensions.Caching.Memory;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace popilot
{
	public class Blackduck
	{
		private readonly HttpClient http;

		public Blackduck(HttpClient http)
		{
			this.http = http;
		}

		private static readonly Regex linkParser = new Regex("<(?<href>.*?)>(;rel=\"(?<rel>.*?)\")?(;title=\"(?<title>.*?)\")?", RegexOptions.Compiled);
		public async IAsyncEnumerable<Project> GetProjects()
		{
			string? nextPage = "projects";
			do
			{
				var response = await this.http.GetAsync(nextPage);

				if (!response.IsSuccessStatusCode)
				{
					var contentString = await response.Content.ReadAsStringAsync();
					throw new BlackduckFetchException(contentString);
				}

				var content = await response.Content.ReadFromJsonAsync<ProjectResponse>();
				foreach (var project in content?.items ?? [])
				{
					yield return project;
				}

				var links = response.Headers
					.GetValues("link")
					.Select(link => linkParser.Match(link))
					.Where(m => m.Success)
					.Select(m => new
					{
						href = m.Groups["href"].Value,
						rel = m.Groups["rel"].Value,
						title = m.Groups["title"].Value,
					})
					.ToList();

				nextPage = links
					.Where(l => l.rel == "paging-next")
					.Select(l => l.href)
					.FirstOrDefault();

			} while (!string.IsNullOrWhiteSpace(nextPage));
		}

		public record ProjectResponse(long totalCount, Project[] items);
		public record Project(string name, string createdBy, DateTimeOffset createdAt, string? updatedBy, DateTimeOffset? updatedAt, Meta _meta);
		public record Meta(string href, Link[] links);
		public record Link(string rel, string href, string? label);

		public class BlackduckFetchException : Exception
		{
			public BlackduckFetchException(string message) : base(message) { }
		}

		public class Auth
		{
			private readonly HttpClient http;
			private readonly IMemoryCache memoryCache;

			public Auth(HttpClient http, IMemoryCache memoryCache)
			{
				this.http = http;
				this.memoryCache = memoryCache;
			}

			public async Task<string> AcquireTokenCached()
			{
				if (!this.memoryCache.TryGetValue("BlackduckBearerToken", out string? bearerToken))
				{
					var authResponse = await this.http.PostAsync("tokens/authenticate", null);
					var stringResponse = await authResponse.Content.ReadAsStringAsync();
					var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(stringResponse)!;
					var expiration = TimeSpan.FromMicroseconds(tokenResponse.expiresInMilliseconds);
					this.memoryCache.Set("BlackduckBearerToken", tokenResponse.bearerToken, expiration);
					bearerToken = tokenResponse.bearerToken;
				}

				return bearerToken!;
			}

			record TokenResponse(string bearerToken, long expiresInMilliseconds);
		}
	}
}
