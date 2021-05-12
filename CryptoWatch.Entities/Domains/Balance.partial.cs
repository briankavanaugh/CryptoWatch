using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoWatch.Entities.Domains {
	partial class Balance {
		[NotMapped]
		public decimal Price { get; set; } = 1m; // set default for cash

		[NotMapped]
		public decimal Value =>
			!this.Amount.HasValue ? 0m : this.Amount.Value * this.Price;

		[NotMapped]
		public decimal BuyBoundary
			=> this.BalanceTarget < 10m ? 0m : this.BalanceTarget - ( this.BalanceTarget * 0.10m );

		[NotMapped]
		public decimal SellBoundary
			=> this.BalanceTarget + ( this.BalanceTarget * 0.10m );

		[NotMapped]
		public bool DisableNotifications { get; set; }
	}
}