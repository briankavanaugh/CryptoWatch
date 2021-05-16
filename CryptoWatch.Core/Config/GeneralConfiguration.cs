namespace CryptoWatch.Core.Config
{
	public sealed class GeneralConfiguration
	{
		public string WatchDirectory { get; set; }
		public decimal CashFloor { get; set; }
		public int SleepInterval { get; set; }
		public int DndStart { get; set; }
		public int DndEnd { get; set; }
		public string CashSymbol { get; set; }
		public string CashSlug { get; set; }
		public string CashName { get; set; }
	}
}