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

		public record TicketsResponse(Ticket[] Tickets, string? NextPage, string? PreviousPage, int Count);
		public record Ticket(string Url, long Id, string? ExternalId, long OrganizationId, long RequesterId, long SubmitterId, long? AssigneeId, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, JsonElement Via, string Type, string Priority, string Status, string Subject, int? ProblemId, bool HasIncidents, string[] Tags, string Description, CustomField[] CustomFields);
		public record CustomField(long Id, object? Value);






		public async Task<Ticket?> GetTicket(int ticketId)
		{
			var ticketResponse = await http.GetAsync($"v2/tickets/{ticketId}.json");
			if (!ticketResponse.IsSuccessStatusCode)
			{
				var content = await ticketResponse.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}
			var ticket = await ticketResponse.Content.ReadFromJsonAsync<TicketResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return ticket?.Ticket;
		}

		public record TicketResponse(Ticket Ticket);

		public async Task<JsonElement> GetTicketRaw(int ticketId)
		{
			var ticketResponse = await http.GetAsync($"v2/tickets/{ticketId}.json");
			if (!ticketResponse.IsSuccessStatusCode)
			{
				var content = await ticketResponse.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}
			var ticket = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return ticket;
		}

		public async IAsyncEnumerable<Comment> GetTicketComments(int ticketId)
		{
			var nextPage = $"v2/tickets/{ticketId}/comments.json";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ZendeskFetchException(content);
				}

				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<CommentsResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

				if (contentNextPage?.Comments != null)
				{
					foreach (var comment in contentNextPage.Comments)
					{
						yield return comment;
					}
				}

				nextPage = contentNextPage?.NextPage;
			}
		}

		public async Task<JsonElement> GetTicketCommentsRaw(int ticketId)
		{
			var nextPage = $"v2/tickets/{ticketId}/comments.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return contentNextPage;
		}

		public record CommentsResponse(Comment[] Comments, string? NextPage, string? PreviousPage, int Count);
		public record Comment(long Id, long AuthorId, string Body, string HtmlBody, string PlainBody, DateTimeOffset CreatedAt, JsonElement Via);






		public async Task<JsonElement> GetTicketFields()
		{
			var ticketResponse = await http.GetAsync($"v2/ticket_fields.json");
			if (!ticketResponse.IsSuccessStatusCode)
			{
				var content = await ticketResponse.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}
			var ticket = await ticketResponse.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return ticket;
		}






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








		public async Task<JsonElement> GetHelpcenterCategoriesRaw()
		{
			var nextPage = $"v2/help_center/categories.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return contentNextPage;
		}

		public async IAsyncEnumerable<Category> GetHelpcenterCategories()
		{
			var nextPage = $"v2/help_center/categories.json";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ZendeskFetchException(content);
				}

				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<CategoriesResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

				if (contentNextPage?.Categories != null)
				{
					foreach (var section in contentNextPage.Categories)
					{
						yield return section;
					}
				}

				nextPage = contentNextPage?.NextPage;
			}
		}

		public record CategoriesResponse(Category[] Categories, string? NextPage, string? PreviousPage, int Count);
		public record Category(long Id, string Url, string HtmlUrl, int Position, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, string Name, string Description, string Locale, string SourceLocale, bool Outdated);

		public async Task<JsonElement?> GetHelpcenterCategoryRaw(long id)
		{
			var nextPage = $"v2/help_center/categories/{id}.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var userResponse = await responseNextPage.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return userResponse;
		}






		public async Task<JsonElement> GetHelpcenterSectionsRaw()
		{
			var nextPage = $"v2/help_center/sections.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return contentNextPage;
		}

		public async IAsyncEnumerable<Section> GetHelpcenterSections(long? categoryId = null)
		{
			var nextPage = categoryId.HasValue ? $"v2/help_center/categories/{categoryId.Value}/sections.json" : "v2/help_center/sections.json";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ZendeskFetchException(content);
				}

				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<SectionsResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

				if (contentNextPage?.Sections != null)
				{
					foreach (var section in contentNextPage.Sections)
					{
						yield return section;
					}
				}

				nextPage = contentNextPage?.NextPage;
			}
		}

		public record SectionsResponse(Section[] Sections, string? NextPage, string? PreviousPage, int Count);
		public record Section(long Id, string Url, string HtmlUrl, long CategoryId, int Position, string Sorting, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, string Name, string Description, string Locale, string SourceLocale, bool Outdated, long? ParentSectionId, string ThemeTemplate);

		public async Task<JsonElement?> GetHelpcenterSectionRaw(long id)
		{
			var nextPage = $"v2/help_center/sections/{id}.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var userResponse = await responseNextPage.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return userResponse;
		}

		public async Task<JsonElement?> GetHelpcenterSectionTranslationsRaw(long id)
		{
			var nextPage = $"v2/help_center/sections/{id}/translations.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var userResponse = await responseNextPage.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return userResponse;
		}








		public async Task<JsonElement> GetHelpcenterArticlesRaw(long sectionId)
		{
			var nextPage = $"v2/help_center/sections/{sectionId}/articles.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return contentNextPage;
		}

		public async IAsyncEnumerable<Article> GetHelpcenterArticles(long? sectionId = null)
		{
			var nextPage = sectionId.HasValue ? $"v2/help_center/sections/{sectionId.Value}/articles.json" : "v2/help_center/articles.json";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ZendeskFetchException(content);
				}

				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<ArticlesResponse>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

				if (contentNextPage?.Articles != null)
				{
					foreach (var section in contentNextPage.Articles)
					{
						yield return section;
					}
				}

				nextPage = contentNextPage?.NextPage;
			}
		}

		public record ArticlesResponse(Article[] Articles, string? NextPage, string? PreviousPage, int Count);
		public record Article(long Id, string Url, string HtmlUrl, long AuthorId, bool Draft, bool Promoted, int Position, long SectionId, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, string Name, string Title, string Locale, string SourceLocale, bool Outdated, DateTimeOffset? EditedAt, long? UserSegmentId, long[] UserSegmentIds, long PermissionGroupId, string[] LabelsNames, string Body);

		public async Task<JsonElement?> GetHelpcenterArticleRaw(long id)
		{
			var nextPage = $"v2/help_center/articles/{id}.json";
			var responseNextPage = await http.GetAsync(nextPage);
			if (!responseNextPage.IsSuccessStatusCode)
			{
				var content = await responseNextPage.Content.ReadAsStringAsync();
				throw new ZendeskFetchException(content);
			}

			var userResponse = await responseNextPage.Content.ReadFromJsonAsync<JsonElement>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
			return userResponse;
		}
	}
}
