using System;
using System.Collections.Generic;

#nullable disable

namespace CryptoWatch.Entities.Domains {
    public partial class CryptoCurrency {
        public CryptoCurrency( ) {
            Transactions = new HashSet<Transaction>( );
        }

        public int Id { get; set; }
        public string Symbol { get; set; }
        public string AltSymbol { get; set; }
        public DateTime Created { get; set; }
        public string Name { get; set; }
        public string Slug { get; set; }
        public long? ExternalId { get; set; }
        public DateTime? AddedToExchange { get; set; }
        public bool? Exclude { get; set; }
        public decimal BalanceTarget { get; set; }
        public decimal BuyTarget { get; set; }
        public decimal SellTarget { get; set; }

        public virtual ICollection<Transaction> Transactions { get; set; }
    }
}
