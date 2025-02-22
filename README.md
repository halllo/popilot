# popilot

Navigating the PO experience on Azure DevOps.

## Getting started using the library

If you want to integrate popilot into your app, install the nuget package and check out its [readme](popilot/README.md).

## Getting started using the CLI

Set up a connection to Azure DevOps by providing your organisation name in the `appsettings.json`. To verify connectivity, ask for a list of available projects.

```bash
popilot.cli.exe get-projects
```

You can generate a status report regarding your current sprint like this:

```bash
popilot.cli.exe get-prio -p "MyProject" -t "MyTeam" -d
```

You can generate release notes markdown and prepend it automatically to a running release notes document on your filesystem. You can also control how links to support tickets are replaced. Invoke it like this:

```bash
popilot.cli.exe get-releasenotes -p "MyProject" -t "MyTeam" -o md --merge-into "$currentFolder\release_notes.md" --replace-by-link "ZD(\d*)=https://zendesk.com/agent/tickets/{0}" --take 3
```

You can get a deployment notification snippet based on your recent deployments to production like this:

```bash
popilot.cli.exe get-recent-deployments -p "MyProject" -t "MyTeam" -r "https://link-to-your-release-notes.com" -d
```

Other than that, there are many more commands available.

## Agent Mode

Delegate work to popilot. Its agent mode will figure out how to combine its many operations smartly to accomplish your goal.

```bash
popilot.cli.exe agent "Create a new task for Manuel to 'Refine Backlog' in the next two sprints (after the current sprint) with 20% of his available capacity."
```

⚠️ Not all popilot operations are available in agent mode yet.

⚠️ Agent mode requires Amazon Bedrock configuration in the `appsettings.json`.

## Contributions

I really hope you find value in popilot. Please feel free to contribute improvements via PRs.

