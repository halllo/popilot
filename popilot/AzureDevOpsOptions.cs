namespace popilot
{
	public class AzureDevOpsOptions
	{
		public Uri BaseUrl { get; set; } = null!;
		public string ClientId { get; set; } = null!;
		public string? TenantId { get; set; }
		public string? Username { get; set; }
		public string? Password { get; set; }
		public string? DefaultProject { get; set; }
		public string? DefaultTeam { get; set; }
		public string? NonRoadmapWorkParentTitle { get; set; }
	}
}
