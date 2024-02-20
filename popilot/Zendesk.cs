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
		public record Organization(string Url, long Id, string Name, string Details, string[] DomainNames, string[] Tags, JsonElement OrganizationFields)
		{
			public string? GetOrgField(string orgField) => OrganizationFields.GetProperty(orgField).GetString();
		}
		public record Meta(bool HasMore, string AfterCursor, string BeforeCursor);
		public record Links(string Prev, string Next);

		public class ZendeskFetchException : Exception
		{
			public ZendeskFetchException(string message) : base(message) { }
		}






		public async IAsyncEnumerable<Ticket> GetTickets(long organisationId)
		{
			var nextPage = $"v2/organizations/{organisationId}/tickets.json";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ZendeskFetchException(content);
				}

				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<TicketsResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

				if (contentNextPage?.Tickets != null)
				{
					foreach (var ticket in contentNextPage.Tickets)
					{
						yield return ticket;
					}
				}

				nextPage = contentNextPage?.NextPage;
			}
		}

		public record TicketsResponse(Ticket[] Tickets, string? NextPage, string? PreviousPage, int Cound);
		public record Ticket(string Url, long Id, string? ExternalId, long OrganizationId, long RequesterId, long SubmitterId, long? AssigneeId, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, JsonElement Via, string Priority, string Status, string Subject, string[] Tags, string Description, CustomField[] CustomFields);
		public record CustomField(long Id, object? Value);






		public async Task<User?> GetUser(long id)
		{
			var nextPage = $"v2/users/{id}.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var userResponse = await responseNextPage.Content.ReadFromJsonAsync<UserResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return userResponse?.User;
		}
		public record UserResponse(User User);
		public record User(string Url, long Id, string Name, string Email, long OrganisationId, string Role);
	}
}
