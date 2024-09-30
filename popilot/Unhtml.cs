using System.Text.RegularExpressions;

namespace popilot
{
	public static class Unhtml
	{
		static Func<string, string> replaceElement(string element, Func<string, string>? replacor = null)
		{
			var matcher = new Regex($"<{element}.*?>(?<inner>.*?)</{element}>", RegexOptions.Compiled | RegexOptions.Singleline);
			return text => matcher.Replace(text, m => m.Success ? (replacor ?? (inner => inner.Trim())).Invoke(m.Groups["inner"].Value.Trim()) : string.Empty);
		}
		static Func<string, string> repeat(int times, Func<string, string> f) => text =>
		{
			var current = text;
			foreach (var ff in Enumerable.Repeat(f, times))
			{
				current = ff(current);
			}
			return current;
		};
		static Func<string, string> replaceDiv = replaceElement("div", inner => $"\n{inner}\n");
		static Func<string, string> replaceSpan = replaceElement("span", inner => $" {inner}");
		static Func<string, string> replacePre = replaceElement("pre");
		static Func<string, string> replaceCode = replaceElement("code", inner => $"\n```json\n{inner.Replace("\n\n\n", "\n").Replace("\n\n", "\n")}\n```\n\n");
		static Func<string, string> replaceU = replaceElement("u");
		static Func<string, string> replaceItalic = replaceElement("i", inner => $"*{inner}*");
		static Func<string, string> replaceBold = replaceElement("b", inner => $"**{inner}**");
		static Func<string, string> replaceLi = replaceElement("li", inner => inner.StartsWith(".\\") ? $"```powershell\n{inner.Replace("<br>", "").Trim()}\n```\n\n" : $"- {inner}\n");
		static Func<string, string> replaceUl = replaceElement("ul", inner => $"\n\n{replaceLi(inner).Trim()}\n");
		static Regex replaceA = new Regex("<a href=\"(?<href>.*?)\".*?>(?<content>.*?)</a>", RegexOptions.Compiled);
		static Regex replaceBr = new Regex("<br.*?>", RegexOptions.Compiled);

		public static string Clean(string text) => text
			.Return()
			.Select(repeat(4, replaceDiv))
			.Select(repeat(4, replaceSpan))
			.Select(s => replaceBr.Replace(s, m => m.Success ? "\n" : string.Empty))
			.Select(replacePre)
			.Select(replaceCode)
			.Select(replaceU)
			.Select(replaceItalic)
			.Select(replaceBold)
			.Select(replaceUl)
			.Select(s => replaceA.Replace(s, m => m.Success ? $"[{m.Groups["content"].Value.Trim()}]({m.Groups["href"].Value.Trim()})" : string.Empty))
			.Select(s => s.Replace("&nbsp;", " "))
			.Select(s => s.Replace("&quot;", "\""))
			.Select(repeat(3, s => s.Replace("  ", " ")))
			.Select(s => s.Replace("\n \n", "\n\n"))
			.Select(s => s.Replace("\n\n\n", "\n\n"))
			.Select(s => s.Trim())
			.Single();
	}
}
