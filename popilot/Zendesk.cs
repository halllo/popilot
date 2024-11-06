using System.Net.Http.Json;
using System.Text.Json;

namespace popilot
{
	public class Zendesk
	{
		private readonly HttpClient http;

		public Zendesk(HttpClient http)
		{
			this.http = http;
		}

		public async IAsyncEnumerable<Organization> GetOrganizations()
		{
			var nextPage = $"v2/organizations.json?page[size]=100";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ZendeskFetchException(content);
				}
				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<OrganizationsResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

				if (contentNextPage?.Organizations != null)
				{
					foreach (var organization in contentNextPage.Organizations)
					{
						yield return organization;
					}
				}

				nextPage = contentNextPage?.Links.Next;
			}
		}

		public async IAsyncEnumerable<Organization> GetOrganizations(string name)
		{
			var nextPage = $"v2/organizations/search.json?name={name}";
			var response = await http.GetAsync(nextPage);

			if (!response.IsSuccessStatusCode)
			{
				var contentString = await response.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(contentString);
			}
			var content = await response.Content.ReadFromJsonAsync<OrganizationsResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

			if (content?.Organizations != null)
			{
				foreach (var organization in content.Organizations)
				{
					yield return organization;
				}
			}
		}

		public record OrganizationsResponse(Organization[] Organizations, Meta Meta, Links Links);
		public record Organization(string Url, long Id, string Name, string Details, string[] DomainNames, string[] Tags, JsonElement OrganizationFields);
		public record Meta(bool HasMore, string AfterCursor, string BeforeCursor);
		public record Links(string Prev, string Next);

		public class ZendeskFetchException : Exception
		{
			public ZendeskFetchException(string message) : base(message) { }
		}
	}
}
