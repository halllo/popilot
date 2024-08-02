using Spectre.Console;
using Spectre.Console.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace popilot.cli
{
	public class Json
	{
		public static string Of(object? o) => o == null ? "<null>" : JsonSerializer.Serialize(o, o.GetType(), new JsonSerializerOptions
		{
			WriteIndented = true,
			Converters =
			{
				new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
			}
		});

		public static void Out(object? o)
		{
			AnsiConsole.Write(o == null
				? new Markup("[grey]<null>[/]")
				: new JsonText(Of(o)));
			Console.WriteLine();
		}
	}
}
