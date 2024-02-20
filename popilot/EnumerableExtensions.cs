using QuickGraph;
using QuickGraph.Algorithms.Observers;
using QuickGraph.Algorithms.Search;

namespace popilot
{
	public static class EnumerableExtensions
	{
		public static IEnumerable<T> Return<T>(this T t)
		{
			yield return t;
		}

		public static IEnumerable<T> NeverNull<T>(this IEnumerable<T>? ts)
		{
			return ts != null ? ts : Enumerable.Empty<T>();
		}

		public static IEnumerable<T> WhereIf<T>(this IEnumerable<T> source, bool condition, Func<T, bool> predicate)
		{
			if (condition)
				return source.Where(predicate);
			else
				return source;
		}

		public static IEnumerable<T> Unfold<T>(this IEnumerable<T> items, Func<T, IEnumerable<T>?> childrenSelector) where T : class
		{
			foreach (var item in items)
			{
				yield return item;
				var children = childrenSelector(item);
				if (children is not null)
				{
					foreach (var child in Unfold(children, childrenSelector))
					{
						yield return child;
					}
				}
			}
		}

		public static IBidirectionalGraph<T, IEdge<T>> Tree<T>(this T root, Func<T, IEnumerable<T>?> childrenSelector, BidirectionalGraph<T, IEdge<T>>? graph = null) where T : class
		{
			graph ??= new BidirectionalGraph<T, IEdge<T>>();

			graph.AddVertex(root);
			var children = childrenSelector(root);
			if (children is not null)
			{
				foreach (var child in children)
				{
					Tree(child, childrenSelector, graph);
					graph.AddEdge(new Edge<T>(root, child));
				}
			}

			return graph;
		}

		public static IBidirectionalGraph<T, IEdge<T>> Tree<T>(this IEnumerable<T> roots, Func<T, IEnumerable<T>?> childrenSelector, BidirectionalGraph<T, IEdge<T>>? graph = null) where T : class
		{
			graph ??= new BidirectionalGraph<T, IEdge<T>>();

			foreach (var root in roots)
			{
				root.Tree(childrenSelector, graph);
			}

			return graph;
		}

		public static IEnumerable<T> Dfs<T>(this IVertexListGraph<T, IEdge<T>> graph, T? root = default, Func<IEnumerable<IEdge<T>>, IEnumerable<IEdge<T>>>? edges = null) where T : notnull
		{
			var dfs = new DepthFirstSearchAlgorithm<T, IEdge<T>>(
				host: null,
				visitedGraph: graph,
				colors: new Dictionary<T, GraphColor>(),
				outEdgeEnumerator: edges != null ? edges.Invoke : outEdges => outEdges);
			var vertexRecorder = new VertexRecorderObserver<T, IEdge<T>>();
			using (vertexRecorder.Attach(dfs))
			{
				if (root != null)
				{
					dfs.Compute(root);
				}
				else
				{
					dfs.Compute();
				}
			}

			return vertexRecorder.Vertices;
		}
	}
}
