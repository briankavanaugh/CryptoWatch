using System;
using CsvHelper.Configuration.Attributes;

namespace CryptoWatch.Services.Models {
	public sealed class FileTransaction {
		[Name( TransactionHeaders.Date )]
		public DateTime Date { get; set; }

		[Name( TransactionHeaders.Destination )]
		public string Destination { get; set; }

		[Name( TransactionHeaders.DestinationAmount )]
		public decimal DestinationAmount { get; set; }

		[Name( TransactionHeaders.DestinationCurrency )]
		public string DestinationCurrency { get; set; }

		[Name( TransactionHeaders.FeeAmount )]
		public decimal? FeeAmount { get; set; }

		[Name( TransactionHeaders.FeeCurrency )]
		public string FeeCurrency { get; set; }

		[Name( TransactionHeaders.Id )]
		public string Id { get; set; }

		[Name( TransactionHeaders.Origin )]
		public string Origin { get; set; }

		[Name( TransactionHeaders.OriginAmount )]
		public decimal OriginAmount { get; set; }

		[Name( TransactionHeaders.OriginCurrency )]
		public string OriginCurrency { get; set; }

		[Name( TransactionHeaders.Status )]
		public string Status { get; set; }

		[Name( TransactionHeaders.Type )]
		public string Type { get; set; }
	}
}