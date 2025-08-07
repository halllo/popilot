using AgentDo;
using AgentDo.Bedrock;
using Amazon.BedrockRuntime;
using Azure.AI.OpenAI;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using OpenAI;
using popilot;
using Serilog;
using System.ClientModel;
using System.Net.Http.Headers;
using System.Reflection;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var host = CreateHostBuilder().Build();
using (var serviceScope = host.Services.CreateScope())
{
	var serviceProvider = serviceScope.ServiceProvider;
	try
	{
		var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

		var actions = typeof(Program).Assembly.GetTypes().Where(t => t.GetCustomAttribute<VerbAttribute>() != null).OrderBy(t => t.Name).ToArray();
		var parserResult = Parser.Default.ParseArguments(args, actions);
		var parsed = parserResult as Parsed<object>;

		if (parsed != null)
		{
			var action = parsed.Value;
			var actionInvocationMethod = action.GetType().GetMethods().Single(m => !m.IsSpecialName && !m.IsStatic && m.DeclaringType == action.GetType());
			try
			{
				var methodArguments = actionInvocationMethod.GetParameters()
					.Select(p => serviceProvider.GetRequiredKeyedService(p.ParameterType, p.GetCustomAttribute<FromKeyedServicesAttribute>()?.Key))
					.ToArray();

				var result = actionInvocationMethod.Invoke(action, methodArguments);
				if (result is Task t)
				{
					await t;
				}
			}
			catch (TargetInvocationException e)
			{
				logger.LogError(e, "Cannot invoke command.");
			}
			catch (Exception e)
			{
				logger.LogError(e, "Unkown error.");
			}
			return 0;
		}
		else
		{
			return 1;
		}
	}
	catch (Exception e)
	{
		var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
		logger.LogError(e, "Something went wrong.");
		return 1;
	}
}

static IHostBuilder CreateHostBuilder()
{
	return Host.CreateDefaultBuilder()
		.ConfigureAppConfiguration(cfg =>
		{
			cfg.AddJsonFile("appsettings.local.json", optional: true);
		})
		.UseSerilog((ctx, cfg) =>
		{
			cfg.ReadFrom.Configuration(ctx.Configuration);
		})
		.ConfigureServices((ctx, services) =>
		{
			var config = ctx.Configuration;
			services.Configure<AzureDevOpsOptions>(o =>
			{
				o.BaseUrl = new Uri(config["AzureDevOps"]!);
				o.TenantId = config["MicrosoftTenantId"];
				o.ClientId = config["AzureDevOpsClientId"]!;
				o.Username = config["AzureDevOpsUser2"];
				o.Password = config["AzureDevOpsPassword"];
				o.DefaultProject = config["DefaultProject"];
				o.DefaultTeam = config["DefaultTeam"];
				o.NonRoadmapWorkParentTitle = config["NonRoadmapWorkParentTitle"];
				o.DontLogAccount = config.GetValue<bool>("DontLogAccount");
			});
			services.AddScoped<AzureDevOps>();

			services.AddSingleton<Microsoft365.GraphClientAuthProvider>();
			services.AddSingleton(sp =>
			{
				var authProvider = sp.GetRequiredService<Microsoft365.GraphClientAuthProvider>();
				return new GraphServiceClient(authProvider);
			});
			services.AddSingleton<Microsoft365>();

			services.AddHttpClient<Zendesk>().ConfigureHttpClient(h =>
			{
				var zendeskSubdomain = config["ZendeskSubdomain"];
				var zendeskEmail = config["ZendeskEmail"];
				var zendeskApiToken = config["ZendeskApiToken"];
				var authToken = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{zendeskEmail}/token:{zendeskApiToken}"));
				h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
				h.BaseAddress = new Uri($"https://{zendeskSubdomain}.zendesk.com/api/");
			});

			services.AddHttpClient<Productboard>().ConfigureHttpClient(h =>
			{
				var authToken = config["ProductboardApiToken"];
				h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authToken);
				h.BaseAddress = new Uri($"https://api.productboard.com/");
			});

			services.AddMemoryCache();
			services.AddHttpClient<Blackduck.Auth>().ConfigureHttpClient(h =>
			{
				h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", config["BlackduckApiToken"]);
				h.BaseAddress = new Uri($"https://{config["BlackduckSubdomain"]}.app.blackduck.com/api/");
			});
			services.AddHttpClient<Blackduck>().ConfigureHttpClient(h =>
			{
				h.BaseAddress = new Uri($"https://{config["BlackduckSubdomain"]}.app.blackduck.com/api/");
			}).AddHttpMessageHandler(sp => new HeaderAddingDelegationHandler(beforeSend: async request =>
			{
				if (request.Headers.Authorization == null)
				{
					var blackduckAuthClient = sp.GetRequiredService<Blackduck.Auth>();
					var token = await blackduckAuthClient.AcquireTokenCached();
					request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
				}
			}));

			services.AddSingleton(sp => new OpenAiService(
				Client: string.IsNullOrWhiteSpace(config["OpenAiApiKey"]) ? null : new OpenAIClient(
					new ApiKeyCredential(config["OpenAiApiKey"]!),
					new OpenAIClientOptions()),
				ModelName: config["OpenAiModelName"]!));
			services.AddSingleton(sp => new AzureOpenAiService(
				Client: string.IsNullOrWhiteSpace(config["AzureOpenAiEndpoint"]) ? null : new AzureOpenAIClient(
					new Uri(config["AzureOpenAiEndpoint"]!),
					new ApiKeyCredential(config["AzureOpenAiKey"]!), new AzureOpenAIClientOptions()),
				DeploymentName: config["AzureOpenAiDeploymentName"]!));
			services.AddSingleton<IAi>(sp => sp.GetRequiredService<AzureOpenAiService>());

			if (!string.IsNullOrWhiteSpace(config["AWSBedrockAccessKeyId"]))
			{
				services.AddSingleton<IAmazonBedrockRuntime>(sp => new AmazonBedrockRuntimeClient(
					awsAccessKeyId: config["AWSBedrockAccessKeyId"]!,
					awsSecretAccessKey: config["AWSBedrockSecretAccessKey"]!,
					region: Amazon.RegionEndpoint.GetBySystemName(config["AWSBedrockRegion"]!)));

				services.AddKeyedSingleton<IAgent, BedrockAgent>("bedrock");
				services.Configure<BedrockAgentOptions>(o =>
				{
					o.ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0";
					o.Streaming = true;
				});
			}
		});
}

