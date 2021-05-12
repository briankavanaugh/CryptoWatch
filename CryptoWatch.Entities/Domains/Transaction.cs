using System;
using System.Collections.Generic;

#nullable disable

namespace CryptoWatch.Entities.Domains {
    public partial class Transaction {
        public int Id { get; set; }
        public string ExternalId { get; set; }
        public int CryptoCurrencyId { get; set; }
        public decimal Amount { get; set; }
        public string Destination { get; set; }
        public string Origin { get; set; }
        public string Status { get; set; }
        public string Type { get; set; }
        public DateTime TransactionDate { get; set; }
        public DateTime Created { get; set; }

        public virtual CryptoCurrency CryptoCurrency { get; set; }
    }
}