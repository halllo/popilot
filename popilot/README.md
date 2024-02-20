# popilot

Enabling your app to navigate the PO experience on Azure DevOps.

```powershell
dotnet add package popilot
```

Register it easily in your service collection:

```csharp
services.Configure<AzureDevOpsOptions>(o =>
{
    o.BaseUrl = new Uri(config["AzureDevOps"]!);
    o.ClientId = config["AzureDevOpsClientId"]!;
    o.TenantId = config["MicrosoftTenantId"];
    o.Username = config["AzureDevOpsUser2"];
    o.Password = config["AzureDevOpsPassword"];
    o.DefaultProject = config["DefaultProject"];
    o.DefaultTeam = config["DefaultTeam"];
});
services.AddScoped<AzureDevOps>();
```

If you leave username and password `null`, popilot will attempt to acquire a user based access token via the device flow. This is perfect for CLI applications.

You can now interact with Azure DevOps through method calls via your injected instance of `AzureDevOps`.

