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
			=> this.BalanceTarget < 10m ? 0m : this.BalanceTarget - ( this.BalanceTarget * this.BuyTarget );

		[NotMapped]
		public decimal BuyLimit
			=> !this.Amount.HasValue ? 0m : this.BuyBoundary / this.Amount.Value;

		[NotMapped]
		public decimal SellBoundary
			=> this.BalanceTarget + ( this.BalanceTarget * this.SellTarget );

		[NotMapped]
		public decimal SellLimit
			=> !this.Amount.HasValue ? 0m : this.SellBoundary / this.Amount.Value;

		[NotMapped]
		public bool DisableNotifications { get; set; }
	}
}