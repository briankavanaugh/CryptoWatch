#nullable disable

namespace CryptoWatch.Entities.Domains {
    public partial class Balance {
        public int Id { get; set; }
        public string Symbol { get; set; }
        public string AltSymbol { get; set; }
        public string Name { get; set; }
        public bool Exclude { get; set; }
        public decimal? Amount { get; set; }
        public decimal BalanceTarget { get; set; }
        public decimal BuyTarget { get; set; }
        public decimal SellTarget { get; set; }
    }
}