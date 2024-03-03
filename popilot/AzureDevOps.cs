using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.Core.WebApi.Types;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using QuickGraph;
using QuickGraph.Algorithms;
using QuickGraph.Algorithms.Observers;
using QuickGraph.Algorithms.Search;

namespace popilot
{
	public sealed class AzureDevOps : IDisposable
	{
		public readonly IOptions<AzureDevOpsOptions> options;
		private readonly ILogger<AzureDevOps> logger;
		private VssConnection? connection;
		public WorkItemTrackingHttpClient? workItemClient;
		public WorkHttpClient? backlogClient;
		public ProjectHttpClient? projectClient;
		public BuildHttpClient? buildClient;

		private static async Task<AuthenticationResult> AcquireAccessToken(string tenantId, string clientId, string? username, string? password, ILogger<AzureDevOps> logger)
		{
			const string azureDevOpsResource = "499b84ac-1321-427f-aa17-267ca6975798";

			var authClient = PublicClientApplicationBuilder.Create(clientId).WithTenantId(tenantId)
				.WithRedirectUri("http://localhost")
				.Build();

			if (username != null && password != null)
			{
				logger.LogInformation("Login as {Username}", username);
				var result = await authClient.AcquireTokenByUsernamePassword(new[] { azureDevOpsResource + "/.default" }, username, password).ExecuteAsync();
				return result;
			}
			else
			{
				var storageProperties = new StorageCreationPropertiesBuilder("msal.cache", ".").Build();
				var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
				cacheHelper.RegisterCache(authClient.UserTokenCache);

				try
				{
					var accounts = await authClient.GetAccountsAsync();
					logger.LogInformation("Attempting silent login: {Accounts}", accounts);
					var result = await authClient.AcquireTokenSilent(new[] { azureDevOpsResource + "/.default" }, accounts.FirstOrDefault()).ExecuteAsync();
					return result;
				}
				catch (Exception)
				{
					logger.LogInformation("Interactive login required");
					var result = await authClient.AcquireTokenInteractive(new[] { azureDevOpsResource + "/.default" }).ExecuteAsync();
					return result;
				}
			}
		}

		private static VssConnection LoginWithAccessToken(Uri baseUrl, string accessToken)
		{
			var token = new VssAadToken("bearer", accessToken);
			var credentials = new VssAadCredential(token);
			var connection = new VssConnection(baseUrl, credentials);
			return connection;
		}

		public AzureDevOps(IOptions<AzureDevOpsOptions> options, ILogger<AzureDevOps> logger)
		{
			this.options = options;
			this.logger = logger;
		}

		private void SetConnection(VssConnection connection)
		{
			this.connection = connection;
			this.workItemClient = connection.GetClient<WorkItemTrackingHttpClient>();
			this.backlogClient = connection.GetClient<WorkHttpClient>();
			this.buildClient = connection.GetClient<BuildHttpClient>();
			this.projectClient = connection.GetClient<ProjectHttpClient>();
		}

		public void Dispose()
		{
			this.backlogClient?.Dispose();
			this.workItemClient?.Dispose();
			this.connection?.Dispose();
		}

		string? azureDevOpsAccessToken;
		public async Task Init()
		{
			if (this.connection == null)
			{
				if (azureDevOpsAccessToken == null)
				{
					var authenticationResult = await AcquireAccessToken(this.options.Value.TenantId, this.options.Value.ClientId, this.options.Value.Username, this.options.Value.Password, this.logger);
					azureDevOpsAccessToken = authenticationResult.AccessToken;
				}

				this.SetConnection(LoginWithAccessToken(options.Value.BaseUrl, azureDevOpsAccessToken));
			}
		}
















		public async Task<IReadOnlyCollection<(Guid id, string name)>> GetProjects()
		{
			await this.Init();

			var projects = Enumerable.Empty<TeamProjectReference>();

			string? continuationToken = null;
			do
			{
				var projectsResponse = await projectClient!.GetProjects(continuationToken: continuationToken);
				projects = projects.Concat(projectsResponse);
				continuationToken = projectsResponse.ContinuationToken;
			} while (continuationToken != null);

			return projects.Select(p => (p.Id, p.Name)).ToList();
		}


		public async Task<Build[]> GetBuilds(Guid projectId, string buildDefinitionName, string tagFilter = default, CancellationToken cancellationToken = default)
		{
			await this.Init();

			var buildDefinitions = await buildClient!.GetDefinitionsAsync(projectId, queryOrder: DefinitionQueryOrder.DefinitionNameAscending, cancellationToken: cancellationToken);
			var buildDefinition = buildDefinitions.Where(d => d.Name == buildDefinitionName).Single();
			var builds = await buildClient.GetBuildsAsync(projectId, new[] { buildDefinition.Id }, cancellationToken: cancellationToken);
			var filteredBuilds = builds.Where(b => b.Status == BuildStatus.Completed && b.Result == BuildResult.Succeeded && (tagFilter == null || b.Tags.Contains(tagFilter)));
			return filteredBuilds.ToArray();
		}


		public async Task<BuildArtifact[]> GetBuildArtifacts(Guid projectId, int buildId, string? artifactFilter = default, CancellationToken cancellationToken = default)
		{
			await this.Init();

			var artifacts = await buildClient!.GetArtifactsAsync(projectId, buildId, cancellationToken: cancellationToken);
			var filteredArtifacts = artifacts.Where(a => artifactFilter == null || a.Name == artifactFilter);
			return filteredArtifacts.ToArray();
		}


		public async Task<Stream> GetBuildArtifact(Guid projectId, int buildId, int artifactId, CancellationToken cancellationToken)
		{
			await this.Init();

			var artifacts = await buildClient!.GetArtifactsAsync(projectId, buildId, cancellationToken: cancellationToken);
			var artifact = artifacts.Where(a => a.Id == artifactId).Single();
			try
			{
				var stream = await buildClient.GetArtifactContentZipAsync(projectId, buildId, artifact.Name, cancellationToken: cancellationToken);
				return stream;
			}
			catch (VssServiceResponseException e) when (e.Message == "Found") //https://developercommunity.visualstudio.com/t/exception-is-being-thrown-for-getartifactcontentzi/1270336
			{
				var handler = new OptionedHttpMessageHandler(this.connection!.InnerHandler, disposeHandler: false, o => o.Set(new HttpRequestOptionsKey<HttpCompletionOption>("MS.VS.HttpCompletionOption"), HttpCompletionOption.ResponseHeadersRead));
				using (var client = new HttpClient(handler))
				{
					client.Timeout = TimeSpan.FromMinutes(60);
					var response = await client.GetAsync(artifact.Resource.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
					response.EnsureSuccessStatusCode();
					var stream = await response.Content.ReadAsStreamAsync();
					return stream;
				}
			}
		}
		private class OptionedHttpMessageHandler : HttpMessageHandler
		{
			private readonly HttpMessageInvoker innerInvoker;
			private readonly Action<HttpRequestOptions> option;

			public OptionedHttpMessageHandler(HttpMessageHandler innerHandler, bool disposeHandler, Action<HttpRequestOptions> option)
			{
				this.innerInvoker = new HttpMessageInvoker(innerHandler, disposeHandler);
				this.option = option;
			}
			protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
			{
				this.option(request.Options);
				var response = await innerInvoker.SendAsync(request, cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
				return response;
			}

			protected override void Dispose(bool disposing)
			{
				this.innerInvoker.Dispose();
			}
		}






























		public async Task<List<int>> GetBacklogOrder(string project, string team, string categoryReferenceName = "Microsoft.FeatureCategory", CancellationToken cancellationToken = default)
		{
			await this.Init();
			var b = await this.backlogClient!.GetBacklogLevelWorkItemsAsync(new TeamContext(project, team), categoryReferenceName);
			var workItemsIds = b.WorkItems.Select(wi => wi.Target.Id).ToList();

			return workItemsIds;
		}

		public async Task<IVertexListGraph<IWorkItemDto, IEdge<IWorkItemDto>>> GetWorkItemWithChildren(int workItemId, CancellationToken cancellationToken = default)
		{
			await this.Init();

			var rootsIds = new[] { workItemId };
			var roots = Map(await workItemClient!.GetWorkItemsAsync(rootsIds, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken));

			IDictionary<int, IWorkItemDto> workItemDictionary;
			var childrenIds = roots.SelectMany(r => r.ChildrenIds).ToArray();
			if (childrenIds.Any())
			{
				var children = Map(await childrenIds.Paged(200, page => workItemClient.GetWorkItemsAsync(page, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken)));

				var childrenChildrenIds = children.SelectMany(c => c.ChildrenIds).ToArray();
				if (childrenChildrenIds.Any())
				{

					var childrenChildren = Map(await childrenChildrenIds.Paged(200, page => workItemClient.GetWorkItemsAsync(page, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken)));

					var childrenChildrenChildrenIds = childrenChildren.SelectMany(c => c.ChildrenIds).ToArray();
					if (childrenChildrenChildrenIds.Any())
					{
						var childrenChildrenChildren = Map(await childrenChildrenChildrenIds.Paged(200, page => workItemClient.GetWorkItemsAsync(page, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken)));
						workItemDictionary = roots.Concat(children).Concat(childrenChildren).Concat(childrenChildrenChildren).ToDictionary(w => w.Id, w => (IWorkItemDto)w);
					}
					else
					{
						workItemDictionary = roots.Concat(children).Concat(childrenChildren).ToDictionary(w => w.Id, w => (IWorkItemDto)w);
					}
				}
				else
				{
					workItemDictionary = roots.Concat(children).ToDictionary(w => w.Id, w => (IWorkItemDto)w);
				}
			}
			else
			{
				workItemDictionary = roots.ToDictionary(w => w.Id, w => (IWorkItemDto)w);
			}

			var graph = new BidirectionalGraph<IWorkItemDto, IEdge<IWorkItemDto>>();
			graph.AddVertexRange(workItemDictionary.Values);
			graph.AddEdgeRange(workItemDictionary.Values.SelectMany(parent => parent.ChildrenIds
				.Where(c => workItemDictionary.ContainsKey(c))
				.Select(childId => new Edge<IWorkItemDto>(source: parent, target: workItemDictionary[childId]))));

			return graph;
		}


		private Task<List<WorkItemDto>> GetWorkItemsWithParents(IEnumerable<WorkItemReference> workItemReferences, CancellationToken cancellationToken) => GetWorkItemsWithParents(workItemReferences.Select(r => r.Id), cancellationToken);
		private async Task<List<WorkItemDto>> GetWorkItemsWithParents(IEnumerable<int> ids, CancellationToken cancellationToken)
		{
			List<WorkItem> workItems = new();
			foreach (var page in ids.Chunk(200))
			{
				var workItemsPage = await workItemClient!.GetWorkItemsAsync(page.Select(i => i), expand: WorkItemExpand.Relations, cancellationToken: cancellationToken);
				workItems.AddRange(workItemsPage);
			}

			List<WorkItemDto> mappedWorkItems = new();
			var directWorkItems = Map(workItems);
			mappedWorkItems.AddRange(directWorkItems);
			var parentIds = directWorkItems.Where(i => i.ParentId.HasValue).Select(i => i.ParentId!.Value).ToList();
			if (parentIds.Any())
			{
				var parentWorkItems = await workItemClient!.GetWorkItemsAsync(parentIds, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken);
				var parentDtos = Map(parentWorkItems);
				mappedWorkItems.AddRange(parentDtos);

				var parentParentIds = parentDtos.Where(i => i.ParentId.HasValue).Select(i => i.ParentId!.Value).ToList();
				if (parentParentIds.Any())
				{
					var parentParentWorkItems = await workItemClient.GetWorkItemsAsync(parentParentIds, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken);
					var parentParentDtos = Map(parentParentWorkItems);
					mappedWorkItems.AddRange(parentParentDtos);

					var parentParentParentIds = parentParentDtos.Where(i => i.ParentId.HasValue).Select(i => i.ParentId!.Value).ToList();
					if (parentParentParentIds.Any())
					{
						var parentParentParentWorkItems = await workItemClient.GetWorkItemsAsync(parentParentParentIds, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken);
						var parentParentParentDtos = Map(parentParentParentWorkItems);
						mappedWorkItems.AddRange(parentParentParentDtos);
					}
				}
			}

			foreach (var directWorkItem in directWorkItems)
			{
				WorkItemDto? parent = null;
				WorkItemDto? parentParent = null;
				WorkItemDto? parentParentParent = null;
				if (directWorkItem.ParentId.HasValue)
				{
					parent = mappedWorkItems.FirstOrDefault(m => m.Id == directWorkItem.ParentId);
					if (parent?.ParentId != null)
					{
						parentParent = mappedWorkItems.FirstOrDefault(m => m.Id == parent.ParentId);
						if (parentParent?.ParentId != null)
						{
							parentParentParent = mappedWorkItems.FirstOrDefault(m => m.Id == parentParent.ParentId);
						}
					}
				}

				directWorkItem.ParentTitle = parent?.Title;
				directWorkItem.RootParentTitle = (parentParentParent ?? parentParent ?? parent)?.Title;
				directWorkItem.RootParentTargetDate = (parentParentParent ?? parentParent ?? parent ?? directWorkItem).TargetDate;
				directWorkItem.RootParentState = (parentParentParent ?? parentParent ?? parent ?? directWorkItem).State;
			}

			return directWorkItems;
		}

		private static List<WorkItemDto> Map(List<WorkItem> workItems)
		{
			return workItems.Select(w => new WorkItemDto
			{
				Id = w.Id ?? 0,
				Type = w.Fields.GetValueOrDefault("System.WorkItemType", string.Empty).ToString()!,
				Title = w.Fields.GetValueOrDefault("System.Title", string.Empty).ToString()!,
				State = w.Fields.GetValueOrDefault("System.State", string.Empty).ToString()!,
				Reason = w.Fields.GetValueOrDefault("System.Reason", string.Empty).ToString()!,
				StoryPoints = int.TryParse(w.Fields.GetValueOrDefault("Microsoft.VSTS.Scheduling.StoryPoints", string.Empty).ToString(), out var parsed) ? parsed : null,
				TeamProject = w.Fields.GetValueOrDefault("System.TeamProject", string.Empty).ToString()!,
				AreaPath = w.Fields.GetValueOrDefault("System.AreaPath", string.Empty).ToString()!,
				IterationPath = w.Fields.GetValueOrDefault("System.IterationPath", string.Empty).ToString()!,
				CreatedDate = w.Fields.GetCastedValueOrDefault("System.CreatedDate", DateTime.MinValue),
				ChangedDate = w.Fields.GetCastedValueOrDefault("System.ChangedDate", DateTime.MinValue),
				ResolvedDate = w.Fields.GetCastedValueOrDefault("Microsoft.VSTS.Common.ResolvedDate", default(DateTime?)),
				ClosedDate = w.Fields.GetCastedValueOrDefault("Microsoft.VSTS.Common.ClosedDate", default(DateTime?)),
				StackRank = w.Fields.GetCastedValueOrDefault("Microsoft.VSTS.Common.StackRank", double.NaN),
				Customers = w.Fields.GetValueOrDefault("Custom.Customer3", string.Empty).ToString()!.Split(';', StringSplitOptions.RemoveEmptyEntries),
				CustomerInformation = w.Fields.GetCastedValueOrDefault("Custom.CustomerInformation", string.Empty).ToString(),
				ReleaseNotes = w.Fields.GetCastedValueOrDefault("Custom.ReleaseNotes", string.Empty).ToString(),
				Tags = w.Fields.GetCastedValueOrDefault("System.Tags", string.Empty).ToString().Split(";").Select(t => t.Trim()).Where(t => t != string.Empty).ToArray(),
				TargetDate = new[] { w.Fields.GetValueOrDefault("Microsoft.VSTS.Scheduling.TargetDate", null) }.Where(o => o != null).Select(o => (DateTime?)o).FirstOrDefault(),
				ParentId = w.Relations.NeverNull()
					.Where(r => r.Attributes.Any(a => a.Key == "name" && a.Value as string == "Parent") && r.Rel == "System.LinkTypes.Hierarchy-Reverse")
					.Select(r => r.Url.Substring(r.Url.LastIndexOf('/') + 1))
					.Select(p => int.TryParse(p, out int pid) ? pid : (int?)null)
					.FirstOrDefault(),
				ChildrenIds = w.Relations.NeverNull()
					.Where(r => r.Attributes.Any(a => a.Key == "name" && a.Value as string == "Child") && r.Rel == "System.LinkTypes.Hierarchy-Forward")
					.Select(r => r.Url.Substring(r.Url.LastIndexOf('/') + 1))
					.Select(p => int.TryParse(p, out int pid) ? pid : (int?)null)
					.Where(i => i.HasValue)
					.Select(i => i!.Value)
					.ToArray(),
				Url = w.Url,
			}).ToList();
		}

		//public sealed class WorkItemEqualityComparer : IEqualityComparer<IWorkItemDto>
		//{
		//	public static WorkItemEqualityComparer Instance { get; } = new WorkItemEqualityComparer();

		//	public bool Equals(IWorkItemDto? x, IWorkItemDto? y)
		//	{
		//		if (ReferenceEquals(x, y)) return true;
		//		if (ReferenceEquals(x, null)) return false;
		//		if (ReferenceEquals(y, null)) return false;
		//		if (x.GetType() != y.GetType()) return false;
		//		return x.Id == y.Id;
		//	}

		//	public int GetHashCode(IWorkItemDto obj)
		//	{
		//		return obj.Id.GetHashCode();
		//	}
		//}
		public interface IWorkItemDto
		{
			int Id { get; }
			string Type { get; }
			string Title { get; }
			string State { get; }
			string Reason { get; }
			string TeamProject { get; }
			string AreaPath { get; }
			string IterationPath { get; }
			int? StoryPoints { get; }
			DateTime CreatedDate { get; }
			DateTime ChangedDate { get; }
			DateTime? ResolvedDate { get; }
			DateTime? ClosedDate { get; }
			double StackRank { get; }
			string[] Customers { get; }
			string CustomerInformation { get; }
			string ReleaseNotes { get; }
			string[] Tags { get; }
			DateTime? TargetDate { get; }
			int? ParentId { get; }
			string? ParentTitle { get; }
			string? RootParentTitle { get; }
			DateTime? RootParentTargetDate { get; }
			string? RootParentState { get; }
			int[] ChildrenIds { get; }
			string Url { get; }
		}
		private class WorkItemDto : IWorkItemDto
		{
			public int Id { get; set; }
			public string Type { get; set; } = null!;
			public string Title { get; set; } = null!;
			public string State { get; set; } = null!;
			public string Reason { get; set; } = null!;
			public string TeamProject { get; set; } = null!;
			public string AreaPath { get; set; } = null!;
			public string IterationPath { get; set; } = null!;
			public int? StoryPoints { get; set; }
			public DateTime CreatedDate { get; set; }
			public DateTime ChangedDate { get; set; }
			public DateTime? ResolvedDate { get; set; }
			public DateTime? ClosedDate { get; set; }
			public double StackRank { get; set; }
			public string[] Customers { get; set; } = null!;
			public string CustomerInformation { get; set; } = null!;
			public string ReleaseNotes { get; set; } = null!;
			public string[] Tags { get; set; } = null!;
			public DateTime? TargetDate { get; set; }
			public int? ParentId { get; set; }
			public string? ParentTitle { get; set; }
			public string? RootParentTitle { get; set; }
			public DateTime? RootParentTargetDate { get; set; }
			public string? RootParentState { get; set; }
			public int[] ChildrenIds { get; set; } = null!;
			public string Url { get; set; } = null!;
		}


		public async Task<List<TeamSettingsIteration>> GetIterations(string? project = null, string? team = null, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var iterations = await this.backlogClient!.GetTeamIterationsAsync(new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam), cancellationToken: cancellationToken);
			return iterations;
		}

		public async Task<TeamSettingsIteration> GetCurrentIteration(string? project = null, string? team = null, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var iterations = await this.backlogClient!.GetTeamIterationsAsync(new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam), cancellationToken: cancellationToken);
			var currentIteration = iterations.Where(i => i.Attributes.TimeFrame == TimeFrame.Current).Single();
			return currentIteration;
		}

		public Task<IReadOnlyCollection<IWorkItemDto>> GetWorkItems(TeamSettingsIteration iteration, CancellationToken cancellationToken = default)
		{
			return GetWorkItems(wiql: $"select System.Id,System.Title,System.State from workitems where [System.IterationPath] = '{iteration.Path}'", cancellationToken);
		}

		public async Task<IReadOnlyCollection<IWorkItemDto>> GetWorkItems(IEnumerable<int> ids, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var workItems = await this.GetWorkItemsWithParents(ids, cancellationToken);
			return workItems;
		}

		public async Task<IReadOnlyCollection<IWorkItemDto>> GetWorkItems(string wiql, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var queryResults = await workItemClient!.QueryByWiqlAsync(new Wiql { Query = wiql }, cancellationToken: cancellationToken);
			var workItems = await GetWorkItemsWithParents(queryResults.WorkItems, cancellationToken);
			return workItems;
		}


		public async Task<IReadOnlyCollection<WorkItem>> GetWorkItemsRaw(IEnumerable<int> ids, CancellationToken cancellationToken = default)
		{
			await this.Init();
			List<WorkItem> workItems = new();
			foreach (var page in ids.Chunk(200))
			{
				var workItemsPage = await workItemClient!.GetWorkItemsAsync(page.Select(i => i), expand: WorkItemExpand.Relations, cancellationToken: cancellationToken);
				workItems.AddRange(workItemsPage);
			}
			return workItems;
		}

		public async Task<IReadOnlyCollection<StateChange>> GetStateChanges(int id, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var revisions = await workItemClient!.GetRevisionsAsync(id, cancellationToken: cancellationToken);
			var stateChanges = revisions
				.Select(r => new StateChange(
					State: r.Fields.GetValueOrDefault("System.State", string.Empty).ToString()!,
					At: r.Fields.GetCastedValueOrDefault("System.ChangedDate", DateTime.MinValue),
					By: (r.Fields["System.ChangedBy"] as IdentityRef)?.DisplayName ?? string.Empty
				))
				.DistinctUntilChanged(r => r.State)
				.ToList();
			return stateChanges;
		}
		public record StateChange(string State, DateTime At, string By);

		public async Task<IReadOnlyList<IterationWithWorkItems>> GetPastIterationsWithCompletedWorkItems(string? project = null, string? team = null, int? take = null, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var teamContext = new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam);
			var iterations = await this.backlogClient!.GetTeamIterationsAsync(teamContext, cancellationToken: cancellationToken);
			var iterationsWithWorkItemReferences = await iterations
				.Where(iteration => iteration.Attributes.TimeFrame == TimeFrame.Past)
				.TakeLast(take ?? 10)
				.ToAsyncEnumerable()
				.SelectAwait(async iteration =>
				{
					var iterationWorkItems = await backlogClient.GetIterationWorkItemsAsync(teamContext, iteration.Id, cancellationToken: cancellationToken);
					return new IterationWithWorkItemsReferences(iteration, iterationWorkItems.WorkItemRelations.Select(wi => wi.Target).ToList());
				})
				.ToListAsync();

			var workItems = await this.GetWorkItemsWithParents(iterationsWithWorkItemReferences.SelectMany(i => i.WorkItemReferences), cancellationToken);
			var workItemsById = workItems.ToLookup(w => w.Id, w => w as IWorkItemDto);
			var iterationsWithWorkItems = iterationsWithWorkItemReferences
				.Select(i => new IterationWithWorkItems(
					Iteration: i.Iteration,
					WorkItems: i.WorkItemReferences
						.SelectMany(r => workItemsById[r.Id])
						.OrderBy(i => i.StackRank)
						.Where(i => new[] { "Bug", "User Story" }.Contains(i.Type))
						.Where(i => i.State == "Closed")
						.ToList())
				)
				.ToList();

			return iterationsWithWorkItems;
		}
		private record IterationWithWorkItemsReferences(TeamSettingsIteration Iteration, List<WorkItemReference> WorkItemReferences);
		public record IterationWithWorkItems(TeamSettingsIteration Iteration, List<IWorkItemDto> WorkItems);

		public async Task<List<QueryHierarchyItem>> GetQueries(string? project = null, string? team = null)
		{
			var teamContext = new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam);
			await this.Init();
			var queries = await workItemClient!.GetQueriesAsync(teamContext.Project, depth: 2);
			return queries;
		}

		public async Task<IReadOnlyCollection<IWorkItemDto>> GetQueryResultsFlat(Guid queryId, string? project = null, string? team = null, CancellationToken cancellationToken = default)
		{
			var teamContext = new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam);
			await this.Init();
			var query = await workItemClient!.GetQueryAsync(teamContext.Project, queryId.ToString(), QueryExpand.Wiql);

			return await GetWorkItems(wiql: query.Wiql, cancellationToken: cancellationToken);
		}

		public async Task<(QueryHierarchyItem, IBidirectionalGraph<IWorkItemDto, IEdge<IWorkItemDto>>?)> GetQueryResults(Guid queryId, string? project = null, string? team = null, CancellationToken cancellationToken = default)
		{
			var teamContext = new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam);
			await this.Init();
			var queryResults = await workItemClient!.GetQueryAsync(teamContext.Project, queryId.ToString(), QueryExpand.Wiql);
			var results = await workItemClient.QueryByWiqlAsync(new Wiql() { Query = queryResults.Wiql });
			if (results.WorkItemRelations != null)
			{
				var workItems = (await GetWorkItems(results.WorkItemRelations.Select(wr => wr.Target.Id))).ToDictionary(w => w.Id);
				var tree = results.WorkItemRelations
					.Where(e => e.Rel != null)
					.Select(e => e switch
					{
						_ => new Edge<IWorkItemDto>(workItems[e.Source.Id], workItems[e.Target.Id])
					})
					.ToBidirectionalGraph<IWorkItemDto, IEdge<IWorkItemDto>>();

				tree.AddVertexRange(results.WorkItemRelations
					.Where(e => e.Rel == null)
					.Select(e => workItems[e.Target.Id]));

				return (queryResults, tree);
			}
			else
			{
				return (queryResults, null);
			}
		}

		public async Task<(IWorkItemDto? Root, IReadOnlyList<IWorkItemDto>? BacklogWorkItems)> GetBacklogWorkItems(QueryHierarchyItem query)
		{
			var (_, tree) = await GetQueryResults(query.Id);
			var backlogWorkItems = tree?.Dfs().Where(v =>
				(v.Type == "Feature" && !v.ChildrenIds.Any())
				||
				new[] { "User Story", "Bug", "Task" }.Contains(v.Type)
			).ToList();
			return (tree.Roots().FirstOrDefault(), backlogWorkItems);
		}

		public async Task<IReadOnlyList<Priority>> GetPriorities(string? project = null, string? team = null, List<string>? priorityOrder = null)
		{
			var teamToFilterBy = team ?? options.Value.DefaultTeam;
			bool teamFilter(QueryHierarchyItem q) => !q.Name.EndsWith(" Team") || q.Name == teamToFilterBy;
			var queries = await this.GetQueries(project, team);
			var queryTree = queries.Tree(q => q.Children?.WhereIf(teamToFilterBy != null, teamFilter));

			var dfs = new DepthFirstSearchAlgorithm<QueryHierarchyItem, IEdge<QueryHierarchyItem>>(
				host: null,
				visitedGraph: queryTree,
				colors: new Dictionary<QueryHierarchyItem, GraphColor>(),
				outEdgeEnumerator: outEdges => outEdges
			);
			var vertexRecorder = new VertexRecorderObserver<QueryHierarchyItem, IEdge<QueryHierarchyItem>>();
			using (vertexRecorder.Attach(dfs))
			{
				dfs.Compute();
			}

			var priorities = vertexRecorder.Vertices.Where(v => v.Name.StartsWith("Priority - "));
			var orderedPriorities = await priorities
				.OrderByDescending(p => priorityOrder?.IndexOf(p.Name) ?? -1)
				.ThenBy(p => p.Name)
				.ToAsyncEnumerable()
				.SelectAwait(async p =>
				{
					var (root, backlogWorkItems) = await this.GetBacklogWorkItems(p);
					return new Priority(
						Id: p.Id,
						Name: p.Name
							.Replace("Priority - Escalation - ", "Eskalation ")
							.Replace("Priority - Release", "Release"),
						IsRelease: p.Name.StartsWith("Priority - Release"),
						IsEscalation: p.Name.StartsWith("Priority - Escalation"),
						Query: p,
						Root: root,
						WorkItems: backlogWorkItems
					);
				})
				.ToListAsync();

			return orderedPriorities;
		}
		public record Priority(Guid Id, string Name, bool IsRelease, bool IsEscalation, QueryHierarchyItem Query, IWorkItemDto? Root, IReadOnlyList<IWorkItemDto>? WorkItems)
		{
			public int? CountClosed => this.WorkItems?.Where(wi => wi.State == "Closed").Count();
			public int? Count => this.WorkItems?.Where(wi => wi.State != "Removed").Count();
		}
























		public async Task<WorkItem> AddTag(int workItemId, string tag, CancellationToken cancellationToken)
		{
			await this.Init();

			var workItem = await this.workItemClient!.GetWorkItemAsync(workItemId, cancellationToken: cancellationToken);
			var tags = workItem.Fields.GetValueOrDefault("System.Tags", string.Empty).ToString();

			var patchDocument = new JsonPatchDocument
			{
				new JsonPatchOperation()
				{
					Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
					Path = "/fields/System.Tags",
					Value = string.IsNullOrWhiteSpace(tags) ? tag : $"{tags};{tag}",
				}
			};
			var response = await this.workItemClient.UpdateWorkItemAsync(patchDocument, workItemId, cancellationToken: cancellationToken);
			return response;
		}

		public async Task<Comment> AddComment(string project, int workItemId, string text, CancellationToken cancellationToken)
		{
			await this.Init();
			var comment = await this.workItemClient!.AddCommentAsync(new CommentCreate { Text = text }, project, workItemId, cancellationToken: cancellationToken);
			return comment;
		}
	}


	public static class AzureDevOpsExtensions
	{
		public static string UrlHumanReadable(this QueryHierarchyItem queryItem)
		{
			return queryItem.Url.Replace("/_apis/wit/queries/", "/_queries/query/");
		}
		public static string UrlHumanReadable(this WorkItem workItem)
		{
			return workItem.Url.Replace("/_apis/wit/workItems/", "/_workitems/edit/");
		}
		public static string UrlHumanReadable(this AzureDevOps.IWorkItemDto workItem)
		{
			return workItem.Url.Replace("/_apis/wit/workItems/", "/_workitems/edit/");
		}
		public static string UrlHumanReadable(this BuildDefinitionReference pipeline)
		{
			return string.Concat(pipeline.Url.Take(pipeline.Url.LastIndexOf("?revision="))).Replace("/_apis/build/Definitions/", "/_build?definitionId=");
		}
		public static string? GetTagValue(this Build build, string tagPrefix)
		{
			return build.Tags.FirstOrDefault(t => t.StartsWith(tagPrefix + "="))?.Substring((tagPrefix + "=").Length);
		}
	}

	file static class CollectionHelper
	{
		public static async Task<List<TResult>> Paged<T, TResult>(this IEnumerable<T>? all, int pageSize, Func<IEnumerable<T>, Task<List<TResult>>> action)
		{
			List<TResult> results = new();
			if (all != null)
			{
				foreach (var page in all.Chunk(200))
				{
					var pageResults = await action(page);
					results.AddRange(pageResults);
				}
			}
			return results;
		}
	}
}
