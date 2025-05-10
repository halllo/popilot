namespace popilot
{
	public class HeaderAddingDelegationHandler : DelegatingHandler
	{
		private readonly Func<HttpRequestMessage, Task> beforeSend;

		public HeaderAddingDelegationHandler(Func<HttpRequestMessage, Task> beforeSend)
		{
			this.beforeSend = beforeSend;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			await beforeSend(request);
			return await base.SendAsync(request, cancellationToken);
		}
	}
}
