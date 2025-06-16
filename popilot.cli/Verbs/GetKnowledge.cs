using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace popilot.cli.Verbs
{
	[Verb("get-knowledge")]
	class GetKnowledge
	{
		[Option('c', longName: "category", HelpText = "Category name or ID", Required = false)]
		public string? Category { get; set; }

		[Option('s', longName: "section", HelpText = "Section name or ID", Required = false)]
		public string? Section { get; set; }

		[Option('a', longName: "article", HelpText = "Article name or ID", Required = false)]
		public string? Article { get; set; }

		[Option(longName: "include-articles", HelpText = "Include articles", Required = false)]
		public bool IncludeArticles { get; set; }

		[Option(longName: "flat-articles", HelpText = "List articles", Required = false)]
		public bool FlatArticles { get; set; }

		[Option(longName: "raw", HelpText = "Show raw object", Required = false)]
		public bool Raw { get; set; }

		public async Task Do(ILogger<GetKnowledge> logger, Zendesk zendesk)
		{
			if (FlatArticles)
			{
				logger.LogInformation("Loading sections for cross reference...");
				var allSections = await zendesk.GetHelpcenterSections().ToDictionaryAsync(s => s.Id);
				
				logger.LogInformation("Loading articles...");
				await foreach (var article in zendesk.GetHelpcenterArticles()
					.Where(a => string.IsNullOrWhiteSpace(Article) || a.Title.Contains(Article, StringComparison.InvariantCultureIgnoreCase) || a.Id.ToString() == Article)
					)
				{
					var section = allSections.GetValueOrDefault(article.SectionId);
					AnsiConsole.MarkupLine($"[gray]{article.Id}[/] {article.Title.Replace("[", "[[").Replace("]", "]]")} ({article.Locale}) {(article.Draft ? "[cyan]draft[/] " : "")}(section:[{(section != null ? "green" : "strikethrough")}]{article.SectionId}[/])");
				}
			}
			else if (Raw)
			{
				if (!string.IsNullOrWhiteSpace(Article))
				{
					var article = await zendesk.GetHelpcenterArticleRaw(long.Parse(Article));
					Json.Out(article);
				}
				else if (!string.IsNullOrWhiteSpace(Section))
				{
					var section = await zendesk.GetHelpcenterSectionRaw(long.Parse(Section));
					Json.Out(section);
					var translations = await zendesk.GetHelpcenterSectionTranslationsRaw(long.Parse(Section));
					Json.Out(translations);
				}
				else if (!string.IsNullOrWhiteSpace(Category))
				{
					var category = await zendesk.GetHelpcenterCategoryRaw(long.Parse(Category));
					Json.Out(category);
				}
				else
				{
					logger.LogError("No ID provided");
					return;
				}
			}
			else
			{
				var categoriesCount = 0;
				var sectionsCount = 0;
				var articlesCount = 0;

				await foreach (var category in zendesk.GetHelpcenterCategories()
					.Where(c => string.IsNullOrWhiteSpace(Category) || c.Name.Contains(Category, StringComparison.InvariantCultureIgnoreCase) || c.Id.ToString() == Category)
					.OrderBy(s => s.Name))
				{
					categoriesCount++;
					AnsiConsole.MarkupLine($"[gray]{category.Id}[/] {category.Name} [gray]{category.Locale}[/]");

					await foreach (var section in zendesk.GetHelpcenterSections(category.Id)
						.Where(s => string.IsNullOrWhiteSpace(Section) || s.Name.Contains(Section, StringComparison.InvariantCultureIgnoreCase) || s.Id.ToString() == Section)
						.OrderBy(s => s.Name))
					{
						sectionsCount++;
						AnsiConsole.MarkupLine($"- [gray]{section.Id}[/] [bold]{section.Name}[/] [gray]{section.Locale}[/]");

						if (IncludeArticles)
						{
							await foreach (var article in zendesk.GetHelpcenterArticles(section.Id)
								.Where(a => string.IsNullOrWhiteSpace(Article) || a.Name.Contains(Article, StringComparison.InvariantCultureIgnoreCase) || a.Id.ToString() == Article)
								.OrderBy(s => s.Name))
							{
								articlesCount++;
								AnsiConsole.MarkupLine($"\t[gray]{article.Id}[/] {article.Title.Replace("[", "[[").Replace("]", "]]")} [gray]{article.Locale}[/]{(article.Draft ? " [cyan]draft[/]" : "")}");
							}
						}
					}

					Console.WriteLine();
				}

				logger.LogInformation($"{{categoriesCount}} categories with {{sectionsCount}} sections{(IncludeArticles ? $" and {{articlesCount}} articles" : "")}", categoriesCount, sectionsCount, articlesCount);
			}
		}
	}
}
