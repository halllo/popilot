using CommandLine;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;

namespace popilot.cli.Verbs
{
	[Verb("get-stakeholders")]
	class GetStakeholders
	{
		public async Task Do(GraphServiceClient graphClient, ILogger<GetStakeholders> logger)
		{
			var myDrive = await graphClient.Me.Drive.GetAsync();
			logger.LogInformation("My Drive: {Drive} ({ID})", myDrive?.Name, myDrive?.Id);

			//todo read excel file
			throw new NotImplementedException("todo");
		}
	}
}
