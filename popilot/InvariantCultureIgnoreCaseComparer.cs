namespace popilot
{
	public class InvariantCultureIgnoreCaseComparer : IEqualityComparer<string>
	{
		public bool Equals(string? x, string? y) => string.Equals(x, y, StringComparison.InvariantCultureIgnoreCase);
		public int GetHashCode(string obj) => obj.ToLowerInvariant().GetHashCode();
		public static IEqualityComparer<string> Instance { get; } = new InvariantCultureIgnoreCaseComparer();
	}
}
