using CommandLine;
using Microsoft.Extensions.Logging;

namespace popilot.cli.Verbs
{
	[Verb("get-notes")]
	class GetNotes
	{
		[Option(longName: "company", Required = false)]
		public string? CompanyFilter { get; set; }

		public async Task Do(ILogger<GetNotes> logger, Productboard productboard)
		{
			var companies = await productboard.GetCompanies()
				.Where(c => CompanyFilter != null ? c.Name.Contains(CompanyFilter, StringComparison.InvariantCultureIgnoreCase) : true)
				.ToListAsync();

			if (companies.Count < 5)
			{
				foreach (var company in companies)
				{
					Console.WriteLine(company.Name);
					Json.Out(new
					{
						company,
						notes = await productboard.GetNotes(companyId: company.Id).ToListAsync(),
					});
				}
			}
			else
			{
				Json.Out(companies);
			}
		}
	}
}
