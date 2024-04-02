using Azure;
using Azure.AI.OpenAI;
using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using popilot;
using Serilog;
using System.Reflection;

System.Console.OutputEncoding = System.Text.Encoding.UTF8;
var host = CreateHostBuilder().Build();
using (var serviceScope = host.Services.CreateScope())
{
	var serviceProvider = serviceScope.ServiceProvider;
	try
	{
		var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

		var actions = typeof(Program).Assembly.GetTypes().Where(t => t.GetCustomAttribute(typeof(VerbAttribute)) != null).OrderBy(t => t.Name).ToList();
		var parserResult = Parser.Default.ParseArguments(args, actions.ToArray());
		var parsed = parserResult as Parsed<object>;

		if (parsed != null)
		{
			var action = parsed.Value;
			var actionInvocationMethod = action.GetType().GetMethods().Single(m => !m.IsSpecialName && !m.IsStatic && m.DeclaringType == action.GetType());
			try
			{
				var methodArguments = actionInvocationMethod.GetParameters().Select(p => serviceProvider.GetRequiredService(p.ParameterType)).ToArray();
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
				o.TenantId = config["MicrosoftTenantId"]!;
				o.ClientId = config["AzureDevOpsClientId"]!;
				o.Username = config["AzureDevOpsUser2"];
				o.Password = config["AzureDevOpsPassword"];
				o.DefaultProject = config["DefaultProject"];
				o.DefaultTeam = config["DefaultTeam"];
			});
			services.AddScoped<AzureDevOps>();

			services.AddSingleton(sp => new OpenAiService(
				Client: string.IsNullOrWhiteSpace(config["OpenAiApiKey"]) ? null : new OpenAIClient(
					config["OpenAiApiKey"]!, 
					new OpenAIClientOptions()),
				ModelName: config["OpenAiModelName"]!));
			services.AddSingleton(sp => new AzureOpenAiService(
				Client: string.IsNullOrWhiteSpace(config["AzureOpenAiEndpoint"]) ? null : new OpenAIClient(
					new Uri(config["AzureOpenAiEndpoint"]!), 
					new AzureKeyCredential(config["AzureOpenAiKey"]!), new OpenAIClientOptions()),
				DeploymentName: config["AzureOpenAiDeploymentName"]!));
			services.AddSingleton<IAi>(sp => sp.GetRequiredService<AzureOpenAiService>());

			services.AddSingleton<GraphClientAuthProvider>();
			services.AddSingleton(sp =>
			{
				var authProvider = sp.GetRequiredService<GraphClientAuthProvider>();
				return new GraphServiceClient(authProvider);
			});
		});
}

