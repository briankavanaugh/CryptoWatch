namespace CryptoWatch.Core.Config {
	public sealed class IntegrationsConfiguration {
		public string Slack { get; set; }
		public bool SlackEnabled { get; set; }
		public string GoogleSheetsId { get; set; }
		public bool GoogleSheetsEnabled { get; set; }
		public string PushbulletToken { get; set; }
		public bool PushbulletEnabled { get; set; }
	}
}