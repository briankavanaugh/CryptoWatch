using System.ComponentModel.DataAnnotations.Schema;

namespace CryptoWatch.Entities.Domains {
	partial class Balance {
		[NotMapped]
		public decimal Price { get; set; } = 1m; // set default for cash

		[NotMapped]
		public decimal Value =>
			!this.Amount.HasValue ? 0m : this.Amount.Value * this.Price;

		[NotMapped]
		public decimal NotifiedAt { get; set; } = 100m;

		[NotMapped]
		public decimal BuyBoundary 
			=> this.NotifiedAt < 10m ? 0m : this.NotifiedAt - 10m;

		[NotMapped]
		public decimal SellBoundary
			=> this.NotifiedAt + 10m;
	}
}