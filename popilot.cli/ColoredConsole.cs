using popilot;
using QuickGraph;
using QuickGraph.Algorithms.Observers;
using QuickGraph.Algorithms.Search;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Serialization;

public static class ColoredConsole
{
	public static void Print(string text, ConsoleColor color, bool sameLine = false)
	{
		var colorBefore = Console.ForegroundColor;
		Console.ForegroundColor = color;
		if (sameLine)
		{
			Console.Write(text);
		}
		else
		{
			Console.WriteLine(text);
		}
		Console.ForegroundColor = colorBefore;
	}

	public static void Boring(string text, bool sameLine = false) => Print(text, ConsoleColor.Gray, sameLine);
	public static void Info(string text, bool sameLine = false) => Print(text, ConsoleColor.Cyan, sameLine);
	public static void Success(string text, bool sameLine = false) => Print(text, ConsoleColor.Green, sameLine);
	public static void Warning(string text, bool sameLine = false) => Print(text, ConsoleColor.Yellow, sameLine);
	public static void Fail(string text, bool sameLine = false) => Print(text, ConsoleColor.Red, sameLine);

	public static void Heading(string text) => Print("\n\n\t" + text + "\n" + new string('=', 100), ConsoleColor.Magenta, sameLine: false);




	public static string Json(object? o, bool indented = true) => o == null ? "<null>" : JsonSerializer.Serialize(o, o.GetType(), new JsonSerializerOptions
	{
		WriteIndented = indented,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
	});





	public static void Display(AzureDevOps.IWorkItemDto workItem)
	{
		switch (workItem.Type)
		{
			case "Bug": Fail($"{workItem.Id}", sameLine: true); break;
			case "User Story": Info($"{workItem.Id}", sameLine: true); break;
			default: Boring($"{workItem.Id}", sameLine: true); break;
		}
		Boring($" {workItem.Title} ", sameLine: true);
		switch (workItem.State)
		{
			case "Closed": Success(workItem.State); break;
			case "Resolved": Info(workItem.State); break;
			case "Active": Warning(workItem.State); break;
			default: Boring(workItem.State); break;
		}
	}

	private static string Display2Core(AzureDevOps.IWorkItemDto workItem)
	{
		string type = workItem.Type switch
		{
			"Epic" => "👑",
			"Feature" => "💎",
			"Bug" => "💥",
			"User Story" => "🗣️",
			"Task" => "📋",
			_ => workItem.Type,
		};
		string state = workItem.State switch
		{
			"Closed" => $"[green]{workItem.State}[/]",
			"Resolved" => $"[cyan]{workItem.State}[/]",
			"Active" => $"[yellow]{workItem.State}[/]",
			_ => $"[gray]{workItem.State}[/]",
		};
		return $"{type}[link={workItem.UrlHumanReadable()}][gray]{workItem.Id}[/][/] {workItem.Title} [gray][[[/]{state}[gray]]][/]";
	}

	public static Markup Display2(AzureDevOps.IWorkItemDto workItem)
	{
		return new Markup(Display2Core(workItem));
	}

	public static Markup Display2WithRemainingWork(AzureDevOps.IWorkItemDto workItem)
	{
		var remainingWork = workItem.RemainingWork != null ? $" [bold]{workItem.RemainingWork}h[/]" : string.Empty;
		return new Markup(Display2Core(workItem) + remainingWork);
	}

	public static void Display<T>(IBidirectionalGraph<T, IEdge<T>> tree, T root, Func<T, Markup> displayName) where T : notnull
	{
		var dfs = new DepthFirstSearchAlgorithm<T, IEdge<T>>(
				host: null,
				visitedGraph: tree,
				colors: new Dictionary<T, GraphColor>(),
				outEdgeEnumerator: outEdges => outEdges);
		var vertexRecorder = new EdgeRecorderObserver<T, IEdge<T>>();
		using (vertexRecorder.Attach(dfs))
		{
			dfs.Compute(root);
		}

		var displayTreeRoot = new Tree(displayName(root)).Style(new Style(foreground: Color.Grey));
		Dictionary<T, IHasTreeNodes> treeNodes = new();
		foreach (var edge in vertexRecorder.Edges)
		{
			var treeNode = treeNodes.ContainsKey(edge.Source) ? treeNodes[edge.Source] : displayTreeRoot;
			var treeNodeNode = treeNode.AddNode(displayName(edge.Target));
			treeNodes.Add(edge.Target, treeNodeNode);
		}

		AnsiConsole.Write(displayTreeRoot);
	}
}
