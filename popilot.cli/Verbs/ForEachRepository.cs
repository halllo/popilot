using System.Text.RegularExpressions;
using CommandLine;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace popilot.cli.Verbs
{
	[Verb("foreach-repository")]
	class ForEachRepository
	{
		[Option(longName: "report", Required = true, HelpText = "Filename to track progress.")]
		public string ReportFile { get; set; } = null!;

		[Option(longName: "skip", Required = false, HelpText = "File containing repository urls to skip.")]
		public string? SkipFile { get; set; }

		[Value(0, MetaName = "filter", Required = false)]
		public string? Filter { get; set; }

		[Option(longName: "clone-in", Required = false, HelpText = "Directory to clone repositories into.")]
		public string? CloneIn { get; set; }

		[Option(longName: "execute", Required = false, HelpText = "Script to execute per repository.")]
		public string? ScriptToExecute { get; set; }

		public async Task Do(AzureDevOps azureDevOps, ILogger<ForEachRepository> logger, ILogger<GetRepositories> getRepoLogger)
		{
			string report;
			if (!File.Exists(ReportFile))
			{
				logger.LogInformation("Report file {ReportFile} does not exist, creating new report...", ReportFile);

				var getRepos = new GetRepositories();
				getRepos.Filter = Filter;
				getRepos.GenerateDocument = true;
				getRepos.GeneratedDocumentName = ReportFile;
				await getRepos.Do(azureDevOps, getRepoLogger);
				report = File.ReadAllText(ReportFile);
			}
			else
			{
				report = File.ReadAllText(ReportFile);
			}

			var skipUrls = SkipFile != null ? File.ReadAllLines(SkipFile) : [];

			var splitPattern = @"(?s)### .*?(?=\s*?(###|##|$))";
			var objectPattern =
				@"(?s)^" +
				@"### <(?<url>https://.*?)>.*?" +
				@"Repository ID: (?<id>[a-f0-9\-]+).*?" +
				@"Name: (?<name>[\w\.]+).*?" +
				@"(DefaultBranch: (?<branch>[\w/]+).*?)?" +
				@"(DetectDepth: (?<detectDepth>\d+).*?)?" +
				@"(ExcludeDirectories: (?<excludeDirectories>[\w\.,/]+).*?)?" +
				@"RemoteUrl: <(?<remoteUrl>https://.*?)>" +
				@"$";

			var repoObjects = Regex.Matches(report, splitPattern).Select(m => m.Value.Trim()).ToList();
			var nextRepoObjects = AnsiConsole.Progress()
				.Start(ctx =>
				{
					var task = ctx.AddTask("Parsing report", maxValue: repoObjects.Count);
					return repoObjects
						.Select(repo => Regex.Match(repo, objectPattern))//not very efficient but good enough for now.
						.Do(m => { task.Value++; ctx.Refresh(); })
						.Where(m => m.Success)
						.Select(m => new
						{
							Url = m.Groups["url"].Value,
							RepositoryId = m.Groups["id"].Value,
							Name = m.Groups["name"].Value,
							DefaultBranch = m.Groups["branch"].Value,
							DetectDepth = m.Groups["detectDepth"].Value,
							ExcludeDirectories = m.Groups["excludeDirectories"].Value,
							RemoteUrl = m.Groups["remoteUrl"].Value
						})
						.ToList();
				});

			logger.LogInformation("Found {Count}/{Total} processable repositories in {ReportFile}.", nextRepoObjects.Count, repoObjects.Count, ReportFile);
			foreach (var repo in nextRepoObjects)
			{
				void updateReport(string content)
				{
					var updatedRepoFileContent = Regex.Replace(
						input: report,
						pattern: $@"(?s)(### <{Regex.Escape(repo.Url)}>.*?RemoteUrl: <.*?>)",
						evaluator: m => $"{m.Groups[1].Value}\r\n{content}"
					).Trim();
					File.WriteAllText(ReportFile, updatedRepoFileContent);
					report = updatedRepoFileContent;//continue with the updated report
				}

				if (skipUrls.Contains(repo.Url))
				{
					logger.LogInformation("Skipping repository {RepoName} ({RepoId}) as it is in the skip list.", repo.Name, repo.RepositoryId);
					updateReport("Skip");
				}
				else
				{
					if (File.Exists(ScriptToExecute))
					{
						if (Directory.Exists(CloneIn))
						{
							var targetDirectory = Path.Combine(CloneIn!, repo.Name);

							{//clone repo
								if (Directory.Exists(targetDirectory))
								{
									logger.LogInformation("Target directory {TargetDirectory} already exists. Skipping clone.", targetDirectory);
								}
								else
								{
									logger.LogInformation("Cloning {RepoUrl} into {TargetDirectory}...", repo.RemoteUrl, targetDirectory);
									var gitClone = new System.Diagnostics.Process
									{
										StartInfo = new System.Diagnostics.ProcessStartInfo
										{
											FileName = "git",
											Arguments = $"clone {repo.RemoteUrl} \"{targetDirectory}\"",
											RedirectStandardOutput = true,
											RedirectStandardError = true,
											UseShellExecute = false,
											CreateNoWindow = true
										}
									};

									gitClone.Start();
									string output = await gitClone.StandardOutput.ReadToEndAsync();
									string error = await gitClone.StandardError.ReadToEndAsync();
									gitClone.WaitForExit();
									var exitCode = gitClone.ExitCode;
									gitClone.Dispose();

									if (exitCode != 0)
									{
										logger.LogError("Git clone failed with exit code {ExitCode}. Error: {Error}", exitCode, error);
										break;
									}

									logger.LogInformation("Cloning finished.");
								}
							}

							{//process repo
								logger.LogInformation("Processing {TargetDirectory}...", targetDirectory);
								var processing = new System.Diagnostics.Process
								{
									StartInfo = new System.Diagnostics.ProcessStartInfo
									{
										FileName = "powershell",
										Arguments = ScriptToExecute,
										WorkingDirectory = targetDirectory,
										RedirectStandardOutput = true,
										RedirectStandardError = true,
										UseShellExecute = false,
										CreateNoWindow = true
									}
								};

								processing.Start();
								string output = await processing.StandardOutput.ReadToEndAsync();
								string error = await processing.StandardError.ReadToEndAsync();
								processing.WaitForExit();
								var exitCode = processing.ExitCode;
								processing.Dispose();
								if (exitCode != 0)
								{
									logger.LogError("Processing failed with exit code {ExitCode}. Error: {Error}", exitCode, error);
									break;
								}
								else
								{
									updateReport($"Processed at {DateTime.UtcNow:o}\r\n{output.Replace("\r\n", " ").Replace("\n", " ")}");
									logger.LogInformation("Processing finished: {Output}", output);
								}
							}

							{//cleanup
								if (Directory.Exists(targetDirectory))
								{
									logger.LogInformation("Removing {TargetDirectory}...", targetDirectory);
									try
									{
										Directory.Delete(targetDirectory, recursive: true);
										logger.LogInformation("Removing finished.");
									}
									catch (Exception ex1)
									{
										logger.LogDebug("Removing cloned repo failed but we will try again: {Error}", ex1.Message);
										try
										{
											var files = Directory.GetFiles(targetDirectory, "*", SearchOption.AllDirectories);
											foreach (var file in files)
											{
												File.SetAttributes(file, FileAttributes.Normal);
												File.Delete(file);
											}

											Directory.Delete(targetDirectory, recursive: true);
											logger.LogInformation("Removing finished.");
										}
										catch (Exception ex2)
										{
											logger.LogError("Removing cloned repo failed: {Error}", ex2.Message);
										}
									}
								}
								else
								{
									logger.LogInformation("Target directory {TargetDirectory} does not exist. Skipping removal.", targetDirectory);
								}
							}
						}
						else
						{
							logger.LogError("Clone directory {CloneIn} does not exist.", CloneIn);
							break;
						}
					}
					else
					{
						logger.LogInformation("Ignoring {RepoName} ({RepoId})", repo.Name, repo.RepositoryId);
					}
				}
			}
		}
	}
}
