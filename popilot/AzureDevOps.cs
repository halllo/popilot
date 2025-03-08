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
using Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Clients;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;
using QuickGraph;
using QuickGraph.Algorithms;
using QuickGraph.Algorithms.Observers;
using QuickGraph.Algorithms.Search;
using System.Text.RegularExpressions;

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
		public ReleaseHttpClient? releaseClient;

		private static async Task<AuthenticationResult> AcquireAccessToken(string clientId, string? tenantId, string? username, string? password, ILogger<AzureDevOps> logger, bool logAccount = true)
		{
			const string azureDevOpsResource = "499b84ac-1321-427f-aa17-267ca6975798";

			var authClient = PublicClientApplicationBuilder.Create(clientId)
				.WithTenantIdIfNotNullNorEmpty(tenantId)
				.WithDefaultRedirectUri()
				.Build();

			if (username != null && password != null)
			{
				if (logAccount) logger.LogInformation("Login as {Username}", username);
				var result = await authClient.AcquireTokenByUsernamePassword([azureDevOpsResource + "/.default"], username, password).ExecuteAsync();
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
					if (logAccount) logger.LogInformation("Attempting silent login: {Accounts}", accounts);
					var result = await authClient.AcquireTokenSilent([azureDevOpsResource + "/.default"], accounts.FirstOrDefault()).ExecuteAsync();
					return result;
				}
				catch (Exception)
				{
					if (logAccount) logger.LogInformation("Interactive login required");
					var result = await authClient.AcquireTokenInteractive([azureDevOpsResource + "/.default"]).ExecuteAsync();
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
			this.releaseClient = connection.GetClient<ReleaseHttpClient>();
			this.projectClient = connection.GetClient<ProjectHttpClient>();
		}

		public void Dispose()
		{
			this.backlogClient?.Dispose();
			this.workItemClient?.Dispose();
			this.buildClient?.Dispose();
			this.releaseClient?.Dispose();
			this.projectClient?.Dispose();
			this.connection?.Dispose();
		}

		string? azureDevOpsAccessToken;
		public async Task Init()
		{
			if (this.connection == null)
			{
				if (azureDevOpsAccessToken == null)
				{
					var authenticationResult = await AcquireAccessToken(this.options.Value.ClientId, this.options.Value.TenantId, this.options.Value.Username, this.options.Value.Password, this.logger, !this.options.Value.DontLogAccount);
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


		public async Task<Build[]> GetBuilds(Guid projectId, string buildDefinitionName, string? tagFilter = default, CancellationToken cancellationToken = default)
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





		public async Task<IReadOnlyCollection<IWorkItemDto>> GetBuildRelatedWorkItems(string? project, int buildId)
		{
			await this.Init();
			var teamContext = new TeamContext(project ?? this.options.Value.DefaultProject);

			var workItemReferences = await this.buildClient!.GetBuildWorkItemsRefsAsync(teamContext.Project, buildId);
			var workItems = await this.GetWorkItems(workItemReferences.Select(w => int.Parse(w.Id)));
			return workItems;
		}







		public async IAsyncEnumerable<DeployableBuild> GetDeployableBuilds(string? project, string? team, string? pathPrefix = null, int? latestBuilds = null)
		{
			await this.Init();
			var teamContext = new TeamContext(project ?? this.options.Value.DefaultProject);

			var definitions = await this.buildClient!.GetDefinitionsAsync(
				project: teamContext.Project,
				includeLatestBuilds: (latestBuilds ?? 1) == 1,
				path: pathPrefix ?? (team != null ? $"\\{team.Replace("Team", "", StringComparison.InvariantCultureIgnoreCase).Trim()}" : null));

			var recentDeployments = definitions
				.OrderBy(d => d.Name)
				.ToAsyncEnumerable()
				.SelectManyAwait(async definition =>
				{
					Build[] builds = (latestBuilds ?? 1) == 1
						? ([definition.LatestBuild])
						: (await this.buildClient.GetBuildsAsync(project: teamContext.Project, definitions: [definition.Id]))
							.OrderByDescending(b => b.QueueTime)
							.Take(latestBuilds ?? 1)
							.ToArray();

					return builds
						.ToAsyncEnumerable()
						.SelectAwait(async build =>
						{
							var timeline = build?.Id != null ? await this.buildClient.GetBuildTimelineAsync(teamContext.Project, build.Id) : null;
							var lastEvent = timeline?.Records.MaxBy(r => r.FinishTime);
							var stages = timeline?.Records != null ? timeline.Records.Where(r => r.RecordType == "Stage").OrderBy(r => r.Order) : Enumerable.Empty<TimelineRecord>();
							var hasProd = stages.Any(s => s.Name.StartsWith("Production"));
							var successfulOnProd = stages.Any(s => s.Name.StartsWith("Production") && s.State == TimelineRecordState.Completed && s.Result == TaskResult.Succeeded);
							var artifactName = definition.Name;
							return new DeployableBuild(definition, build!, artifactName, timeline!, lastEvent!, stages, hasProd, successfulOnProd);
						});
				});

			await foreach (var recentDeployment in recentDeployments)
			{
				yield return recentDeployment;
			}
		}
		public record DeployableBuild(BuildDefinitionReference definition, Build latestBuild, string artifactName, Timeline timeline, TimelineRecord lastEvent, IEnumerable<TimelineRecord> stages, bool hasProd, bool successfulOnProd);


		public async IAsyncEnumerable<DeployableRelease> GetDeployableReleases(string? project, string? team, string? pathPrefix = null, string? searchText = null)
		{
			await this.Init();
			var teamContext = new TeamContext(project ?? this.options.Value.DefaultProject);

			var releaseDefinitions = await this.releaseClient!.GetReleaseDefinitionsAsync(
				project: teamContext.Project,
				path: pathPrefix ?? (team != null ? $"\\{team.Replace("Team", "", StringComparison.InvariantCultureIgnoreCase).Trim()}" : null),
				searchText: searchText,
				expand: Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts.ReleaseDefinitionExpands.LastRelease);

			var allReleases = releaseDefinitions
				.Select(r => new
				{
					r.Id,
					r.Name,
					r.Path,
					LastReleaseId = r.LastRelease.Id,
					LastReleaseName = r.LastRelease.Name,
				})
				.ToAsyncEnumerable()
				.SelectManyAwait(async releaseDefinition =>
				{
					var releases = await this.releaseClient.GetReleasesAsync(
						project: teamContext.Project,
						definitionId: releaseDefinition.Id,
						expand: Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts.ReleaseExpands.Environments
							  | Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.Contracts.ReleaseExpands.Artifacts);

					var releasesReduced = releases.Select(r => new
					{
						r.Id,
						r.Name,
						OnStaging = r.Environments.Any(e => e.Name.StartsWith("Stag", StringComparison.InvariantCultureIgnoreCase) && e.Status == Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.EnvironmentStatus.Succeeded),
						OnProduction = r.Environments.Any(e => e.Name.StartsWith("Prod", StringComparison.InvariantCultureIgnoreCase) && e.Status == Microsoft.VisualStudio.Services.ReleaseManagement.WebApi.EnvironmentStatus.Succeeded),
						ArtifactName = r.Artifacts.Single().DefinitionReference["definition"].Name,
						ArtifactVersion = r.Artifacts.Single().DefinitionReference["version"].Name,
						LastModifiedOn = r.Environments.Max(e => e.DeploySteps.Any() ? e.DeploySteps.Max(s => s.LastModifiedOn) : (DateTime?)null)
					});

					return releasesReduced.ToAsyncEnumerable();
				})
				.OrderByDescending(r => r.LastModifiedOn)
				.GroupBy(r => r.ArtifactName)
				.SelectAwait(async r => new DeployableRelease
				(
					r.Key,
					await r.Select(e => e.ArtifactVersion).FirstAsync(),
					await r.Select(e => e.OnStaging).FirstAsync(),
					await r.Select(e => e.OnProduction).FirstAsync(),
					await r.Select(e => e.LastModifiedOn).FirstAsync()
				));

			await foreach (var release in allReleases)
			{
				yield return release;
			}
		}
		public record DeployableRelease(string ArtifactName, string ArtifactVersion, bool OnStaging, bool OnProduction, DateTime? LastModifiedOn);



























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
			List<WorkItem> workItems = await ids.Distinct().Paged(200, page => workItemClient!.GetWorkItemsAsync(page, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken));

			List<WorkItemDto> mappedWorkItems = new();
			var directWorkItems = Map(workItems);
			mappedWorkItems.AddRange(directWorkItems);
			var parentIds = directWorkItems.Where(i => i.ParentId.HasValue).Select(i => i.ParentId!.Value).ToList();
			if (parentIds.Any())
			{
				var parentWorkItems = await parentIds.Distinct().Paged(200, page => workItemClient!.GetWorkItemsAsync(page, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken));
				var parentDtos = Map(parentWorkItems);
				mappedWorkItems.AddRange(parentDtos);

				var parentParentIds = parentDtos.Where(i => i.ParentId.HasValue).Select(i => i.ParentId!.Value).ToList();
				if (parentParentIds.Any())
				{
					var parentParentWorkItems = await parentParentIds.Distinct().Paged(200, page => workItemClient!.GetWorkItemsAsync(page, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken));
					var parentParentDtos = Map(parentParentWorkItems);
					mappedWorkItems.AddRange(parentParentDtos);

					var parentParentParentIds = parentParentDtos.Where(i => i.ParentId.HasValue).Select(i => i.ParentId!.Value).ToList();
					if (parentParentParentIds.Any())
					{
						var parentParentParentWorkItems = await parentParentParentIds.Distinct().Paged(200, page => workItemClient!.GetWorkItemsAsync(page, expand: WorkItemExpand.Relations, cancellationToken: cancellationToken));
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
				directWorkItem.ParentType = parent?.Type;
				directWorkItem.ParentTags = parent?.Tags;
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
				AssignedTo = (w.Fields.GetValueOrDefault("System.AssignedTo", default(IdentityRef)) as IdentityRef)?.DisplayName ?? string.Empty,
				State = w.Fields.GetValueOrDefault("System.State", string.Empty).ToString()!,
				Reason = w.Fields.GetValueOrDefault("System.Reason", string.Empty).ToString()!,
				StoryPoints = int.TryParse(w.Fields.GetValueOrDefault("Microsoft.VSTS.Scheduling.StoryPoints", string.Empty).ToString(), out var parsed) ? parsed : null,
				OriginalEstimate = (double?)w.Fields.GetValueOrDefault("Microsoft.VSTS.Scheduling.OriginalEstimate", (double?)null),
				RemainingWork = (double?)w.Fields.GetValueOrDefault("Microsoft.VSTS.Scheduling.RemainingWork", (double?)null),
				CompletedWork = (double?)w.Fields.GetValueOrDefault("Microsoft.VSTS.Scheduling.CompletedWork", (double?)null),
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

		public interface IWorkItemDto
		{
			int Id { get; }
			string Type { get; }
			string Title { get; }
			string AssignedTo { get; }
			string State { get; }
			string Reason { get; }
			string TeamProject { get; }
			string AreaPath { get; }
			string IterationPath { get; }
			int? StoryPoints { get; }
			double? OriginalEstimate { get; }
			double? RemainingWork { get; }
			double? CompletedWork { get; }
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
			string? ParentType { get; }
			string[]? ParentTags { get; }
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
			public string AssignedTo { get; set; } = null!;
			public string State { get; set; } = null!;
			public string Reason { get; set; } = null!;
			public string TeamProject { get; set; } = null!;
			public string AreaPath { get; set; } = null!;
			public string IterationPath { get; set; } = null!;
			public int? StoryPoints { get; set; }
			public double? OriginalEstimate { get; set; }
			public double? RemainingWork { get; set; }
			public double? CompletedWork { get; set; }
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
			public string? ParentType { get; set; }
			public string[]? ParentTags { get; set; }
			public string? RootParentTitle { get; set; }
			public DateTime? RootParentTargetDate { get; set; }
			public string? RootParentState { get; set; }
			public int[] ChildrenIds { get; set; } = null!;
			public string Url { get; set; } = null!;
		}


		public async Task<IReadOnlyList<TeamSettingsIteration>> GetIterations(string? project = null, string? team = null, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var iterations = await this.backlogClient!.GetTeamIterationsAsync(new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam), cancellationToken: cancellationToken);
			return iterations;
		}

		public async Task<IReadOnlyList<TeamSettingsIteration>> GetAllIterations(string? project = null, string? team = null, string? path = null, Regex? pathFilter = null, CancellationToken cancellationToken = default)
		{
			var allIterations = await GetAllClassificationNodesFlat(TreeStructureGroup.Iterations, project, team, path, pathFilter, cancellationToken);
			var iterations = allIterations
				.Where(s => s.HasChildren == false)
				.Select(s => new TeamSettingsIteration
				{
					Attributes = new TeamIterationAttributes
					{
						TimeFrame = s.Attributes == null ? null : ((DateTime)s.Attributes["startDate"], (DateTime)s.Attributes["finishDate"]) switch
						{
							(DateTime start, DateTime end) when end < DateTime.Now => TimeFrame.Past,
							(DateTime start, DateTime end) when start < DateTime.Now && DateTime.Now < end => TimeFrame.Current,
							(DateTime start, DateTime end) when DateTime.Now < start => TimeFrame.Future,
							_ => null,
						},
						StartDate = s.Attributes == null ? null : (DateTime)s.Attributes["startDate"],
						FinishDate = s.Attributes == null ? null : (DateTime)s.Attributes["finishDate"],
					},
					Id = Guid.Empty,
					Name = s.Name,
					Links = s.Links,
					Path = s.Path.Replace("\\Iteration\\", "\\", StringComparison.InvariantCultureIgnoreCase).TrimStart('\\'),
					Url = s.Url,
				})
				.OrderBy(s => s.Attributes.FinishDate)
				.ToList();

			return iterations;
		}

		public async Task<IReadOnlyList<string>> GetAllAreas(string? project = null, string? team = null, CancellationToken cancellationToken = default)
		{
			var areas = await GetAllClassificationNodesFlat(TreeStructureGroup.Areas, project, team, cancellationToken: cancellationToken);
			var areaPaths = areas
				.Select(a => a.Path.Replace("\\Area\\", "\\", StringComparison.InvariantCultureIgnoreCase).TrimStart('\\'))
				.ToList();

			return areaPaths;
		}

		/// <summary>
		/// Provide either project and team or project and path. When providing team, the team name is used to find the path based on conventions.
		/// </summary>
		public async Task<IEnumerable<WorkItemClassificationNode>> GetAllClassificationNodesFlat(TreeStructureGroup treeStructureGroup, string? project = null, string? team = null, string? path = null, Regex? pathFilter = null, CancellationToken cancellationToken = default)
		{
			await this.Init();

			WorkItemClassificationNode root;

			if (path != null)
			{
				root = await this.workItemClient!.GetClassificationNodeAsync(
					project: project ?? options.Value.DefaultProject,
					structureGroup: treeStructureGroup,
					path: path,
					depth: 3);
			}
			else if (team != null)
			{
				root = await this.workItemClient!.GetClassificationNodeAsync(
					project: project ?? options.Value.DefaultProject,
					structureGroup: treeStructureGroup,
					depth: 2);

				var teamAreaPathName = (team ?? options.Value.DefaultTeam ?? "").Replace("Team", "", StringComparison.InvariantCultureIgnoreCase).Trim();
				bool hasTeamPath = root.HasChildren != false && root.Children.Any(c => string.Equals(c.Name, teamAreaPathName, StringComparison.InvariantCultureIgnoreCase));
				if (hasTeamPath)
				{
					root = await this.workItemClient!.GetClassificationNodeAsync(
						project: project ?? options.Value.DefaultProject,
						structureGroup: treeStructureGroup,
						path: teamAreaPathName,
						depth: 2);
				}

				var teamAreaPathNameSpaceless = teamAreaPathName.Replace(" ", string.Empty);
				bool hasTeamPathSpaceless = root.HasChildren != false && root.Children.Any(c => string.Equals(c.Name, teamAreaPathNameSpaceless, StringComparison.InvariantCultureIgnoreCase));
				if (hasTeamPathSpaceless)
				{
					root = await this.workItemClient!.GetClassificationNodeAsync(
						project: project ?? options.Value.DefaultProject,
						structureGroup: treeStructureGroup,
						path: teamAreaPathNameSpaceless,
						depth: 2);
				}
			}
			else
			{
				throw new ArgumentException("Required either 'team' or 'path'.");
			}

			var classificationNodes = EnumerableExtensions
				.Tree([root], parent => parent.Children?.WhereIf(pathFilter != null, p => pathFilter!.IsMatch(p.Path[root.Path.Length..].Trim('\\'))))
				.Dfs(root);

			return classificationNodes;
		}

		public async Task<TeamSettingsIteration> GetCurrentIteration(string? project = null, string? team = null, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var iterations = await GetIterations(project, team, cancellationToken);
			var currentIteration = iterations.Where(i => i.Attributes.TimeFrame == TimeFrame.Current).Single();
			return currentIteration;
		}

		public async Task<TeamSettingsIteration?> GetIteration(string path, string? project = null, string? team = null, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var iterations = await GetIterations(project, team, cancellationToken);
			var iteration = iterations.Where(i => i.Path.StartsWith(path)).SingleOrDefault();
			return iteration;
		}

		public Task<IReadOnlyCollection<IWorkItemDto>> GetWorkItems(TeamSettingsIteration iteration, CancellationToken cancellationToken = default)
		{
			return GetWorkItemsOfIterationPath(iteration.Path);
		}

		public async Task<IReadOnlyCollection<IWorkItemDto>> GetWorkItems(IEnumerable<TeamSettingsIteration> iterations, Func<IWorkItemDto, bool>? filter = null, CancellationToken cancellationToken = default)
		{
			await this.Init();
			return await iterations
				.ToAsyncEnumerable()
				.SelectAwait(async i => await GetWorkItems(i))
				.SelectMany(i => i.ToAsyncEnumerable())
				.Where(w => filter != null ? filter(w) : true)
				.ToListAsync();
		}

		public Task<IReadOnlyCollection<IWorkItemDto>> GetWorkItemsOfIterationPath(string iterationPath, CancellationToken cancellationToken = default)
		{
			return GetWorkItems(wiql: $"select System.Id,System.Title,System.State from workitems where [System.IterationPath] = '{iterationPath}'", cancellationToken);
		}

		public Task<IEnumerable<WorkItemReference>> GetWorkItemReferencesOfIterationPath(string iterationPath, CancellationToken cancellationToken = default)
		{
			return GetWorkItemReferences(wiql: $"select System.Id,System.Title,System.State from workitems where [System.IterationPath] = '{iterationPath}'", cancellationToken);
		}

		public async Task<IReadOnlyCollection<IWorkItemDto>> GetWorkItems(IEnumerable<int> ids, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var workItems = await this.GetWorkItemsWithParents(ids, cancellationToken);
			return workItems;
		}

		public async Task<IReadOnlyCollection<IWorkItemDto>> GetWorkItems(string wiql, CancellationToken cancellationToken = default)
		{
			var workItemReferences = await GetWorkItemReferences(wiql, cancellationToken);
			var workItems = await GetWorkItemsWithParents(workItemReferences, cancellationToken);
			return workItems;
		}

		public async Task<IEnumerable<WorkItemReference>> GetWorkItemReferences(string wiql, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var queryResults = await workItemClient!.QueryByWiqlAsync(new Wiql { Query = wiql }, cancellationToken: cancellationToken);
			return queryResults.WorkItems;
		}

		public async Task<List<WorkItemUpdate>> GetWorkItemHistory(int id, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var updates = await workItemClient!.GetUpdatesAsync(id, cancellationToken: cancellationToken);
			return updates;
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
			var iterations = await GetIterations(project, team, cancellationToken);
			var filteredIterations = iterations
				.Where(i => i.Attributes.TimeFrame == TimeFrame.Past)
				.TakeLast(take ?? 10);

			return await GetIterationsWithCompletedWorkItems(project, team, filteredIterations, cancellationToken);
		}

		public async Task<IReadOnlyList<IterationWithWorkItems>> GetIterationsWithCompletedWorkItems(string? project, string? team, IEnumerable<TeamSettingsIteration> iterations, CancellationToken cancellationToken = default)
		{
			await this.Init();
			var teamContext = new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam);
			var iterationsWithWorkItemReferences = await iterations
				.ToAsyncEnumerable()
				.SelectAwait(async iteration =>
				{
					if (iteration.Id != Guid.Empty)
					{
						var iterationWorkItems = await backlogClient!.GetIterationWorkItemsAsync(teamContext, iteration.Id, cancellationToken: cancellationToken);
						return new IterationWithWorkItemsReferences(iteration, iterationWorkItems.WorkItemRelations.Select(wi => wi.Target).ToList());
					}
					else
					{
						var workItemReferences = await GetWorkItemReferencesOfIterationPath(iteration.Path, cancellationToken);
						return new IterationWithWorkItemsReferences(iteration, workItemReferences.ToList());
					}
				})
				.ToListAsync();

			var workItems = await this.GetWorkItemsWithParents(iterationsWithWorkItemReferences.SelectMany(i => i.WorkItemReferences), cancellationToken);
			var workItemsById = workItems.ToLookup(w => w.Id, w => w as IWorkItemDto);
			var iterationsWithWorkItems = iterationsWithWorkItemReferences
				.Select(i => new IterationWithWorkItems(
					Iteration: i.Iteration,
					WorkItems: i.WorkItemReferences
						.SelectMany(r => workItemsById[r.Id].Where(rr => rr.IterationPath == i.Iteration.Path).Take(1/*only output each item once*/))
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

		public async Task<(IReadOnlyList<IWorkItemDto> Roots, IReadOnlyList<IWorkItemDto>? BacklogWorkItems)> GetBacklogWorkItems(Guid queryId, string? project = null)
		{
			var (_, tree) = await GetQueryResults(queryId, project);
			var backlogWorkItems = tree?
				.Dfs()
				.Where(v =>
					(v.Type == "Feature" && !v.ChildrenIds.Any())
					||
					new[] { "User Story", "Bug", "Task" }.Contains(v.Type)
				)
				.Where(w => w.State != "Removed")
				.ToList();
			return (tree.Roots().ToList(), backlogWorkItems);
		}

		public async Task<(IWorkItemDto? Root, IReadOnlyList<IWorkItemDto>? BacklogWorkItems)> GetBacklogWorkItems(QueryHierarchyItem query)
		{
			var (_, tree) = await GetQueryResults(query.Id);
			var backlogWorkItems = tree?
				.Dfs()
				.Where(v =>
					(v.Type == "Feature" && !v.ChildrenIds.Any())
					||
					new[] { "User Story", "Bug", "Task" }.Contains(v.Type)
				)
				.Where(w => w.State != "Removed")
				.ToList();
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












		public async Task<(TeamCapacity teamCapacity, TeamSettingsDaysOff teamDaysOff)> GetCapacities(string? project, string? team, TeamSettingsIteration iteration)
		{
			await this.Init();
			var teamContext = new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam);

			var capacities = await this.backlogClient!.GetCapacitiesWithIdentityRefAndTotalsAsync(teamContext, iteration.Id);
			var teamDaysOff = await this.backlogClient!.GetTeamDaysOffAsync(teamContext, iteration.Id);
			return (capacities, teamDaysOff);
		}












		public async Task<IWorkItemDto> CreateWorkItem(string? project, string? team,
			TeamSettingsIteration iteration,
			string type,
			string title,
			IdentityRef? assignee,
			decimal? effort,
			int? parent,
			CancellationToken cancellationToken = default)
		{
			await this.Init();
			var teamContext = new TeamContext(project ?? options.Value.DefaultProject, team ?? options.Value.DefaultTeam);
			var areaPath = (await GetAllAreas(teamContext.Project, teamContext.Team))
				.First();//there is no API to get the default area path of a team, so we assume it is the first.

			var patchDocument = new JsonPatchDocument
			{
				new JsonPatchOperation()
				{
					Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
					Path = "/fields/System.Title",
					Value = title,
				},
				new JsonPatchOperation()
				{
					Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
					Path = "/fields/System.AreaPath",
					Value = areaPath,
				},
				new JsonPatchOperation()
				{
					Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
					Path = "/fields/System.IterationPath",
					Value = iteration.Path,
				},
			};

			if (assignee != null)
			{
				patchDocument.Add(new JsonPatchOperation()
				{
					Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
					Path = "/fields/System.AssignedTo",
					Value = assignee,
				});
			}

			if (effort.HasValue)
			{
				patchDocument.Add(new JsonPatchOperation()
				{
					Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
					Path = "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate",
					Value = effort.Value,
				});
				patchDocument.Add(new JsonPatchOperation()
				{
					Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
					Path = "/fields/Microsoft.VSTS.Scheduling.RemainingWork",
					Value = effort.Value,
				});
			}

			if (parent.HasValue)
			{
				var parentWorkItem = (await this.GetWorkItems([parent.Value], cancellationToken)).Single();
				patchDocument.Add(new JsonPatchOperation()
				{
					Operation = Microsoft.VisualStudio.Services.WebApi.Patch.Operation.Add,
					Path = "/relations/-",
					Value = new WorkItemRelation
					{
						Attributes = new Dictionary<string, object> { { "name", "Parent" } },
						Rel = "System.LinkTypes.Hierarchy-Reverse",
						Url = parentWorkItem.Url,
					},
				});
			}

			var newWorkItem = await this.workItemClient!.CreateWorkItemAsync(patchDocument, teamContext.Project, type);
			return Map([newWorkItem]).Single();
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
		public static bool Contains(this IEnumerable<DateRange> dateRange, DateTime date)
		{
			return dateRange.Any(dr => dr.Contains(date));
		}
		public static bool Contains(this DateRange dateRange, DateTime date)
		{
			return dateRange.Start <= date && date <= dateRange.End;
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
