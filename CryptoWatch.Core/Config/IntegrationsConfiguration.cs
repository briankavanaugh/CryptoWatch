namespace CryptoWatch.Core.Config {
	public sealed class IntegrationsConfiguration {
		public string Slack { get; set; }
		public string GoogleSheetsId { get; set; }
		public bool GoogleSheetsEnabled { get; set; }
	}
}