using System.Net.Http.Json;

namespace popilot
{
	public class Productboard
	{
		private readonly HttpClient http;

		public Productboard(HttpClient http)
		{
			this.http = http;
		}

		public class ProductboardFetchException : Exception
		{
			public ProductboardFetchException(string message) : base(message) { }
		}

		public async IAsyncEnumerable<Company> GetCompanies()
		{
			var nextPage = $"companies";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ProductboardFetchException(content);
				}

				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<CompaniesResponse>();

				foreach (var company in contentNextPage?.Data ?? [])
				{
					yield return company;
				}

				if (contentNextPage?.Links?.Next != null)
				{
					nextPage = contentNextPage.Links.Next;
				}
				else
				{
					nextPage = null;
				}
			}
		}

		public record CompaniesResponse(Company[] Data, Links Links);
		public record Company(Guid Id, string Name, string Domain, string Description, string SourceOrigin, Guid SourceRecordId);
		public record Links(string Next);

		public async IAsyncEnumerable<Note> GetNotes()
		{
			var nextPage = $"notes";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ProductboardFetchException(content);
				}

				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<NotesResponse>();

				foreach (var note in contentNextPage?.Data ?? [])
				{
					yield return note;
				}

				if (contentNextPage?.PageCursor != null)
				{
					nextPage = $"notes?pageCursor={contentNextPage.PageCursor}";
				}
				else
				{
					nextPage = null;
				}
			}
		}

		public async IAsyncEnumerable<Note> GetNotes(Guid companyId)
		{
			var nextPage = $"notes?companyId={companyId}";
			while (nextPage != null)
			{
				var responseNextPage = await http.GetAsync(nextPage);
				if (!responseNextPage.IsSuccessStatusCode)
				{
					var content = await responseNextPage.Content.ReadAsStringAsync();
					throw new ProductboardFetchException(content);
				}

				var contentNextPage = await responseNextPage.Content.ReadFromJsonAsync<NotesResponse>();

				foreach (var note in contentNextPage?.Data ?? [])
				{
					yield return note;
				}

				if (contentNextPage?.PageCursor != null)
				{
					nextPage = $"notes?companyId={companyId}&pageCursor={contentNextPage.PageCursor}";
				}
				else
				{
					nextPage = null;
				}
			}
		}

		public record NotesResponse(Note[] Data, string? PageCursor, int TotalResults);
		public record Note(Guid Id, string Title, string Content, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, string State, string DisplayUrl, string ExternalDisplayUrl, string[] Tags, CompanyReference? Company, Owner? Owner, User? User, CreatedBy CreatedBy);
		public record CompanyReference(Guid Id);
		public record Owner(string Name, string Email);
		public record CreatedBy(Guid Id, string Name, string Email);
		public record User(Guid Id);
	}
}
