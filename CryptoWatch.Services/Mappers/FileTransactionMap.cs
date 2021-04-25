using CryptoWatch.Services.Models;
using CsvHelper.Configuration;

namespace CryptoWatch.Services.Mappers {
	public sealed class FileTransactionMap : ClassMap<FileTransaction> {
		public FileTransactionMap( ) {
			this.Map( m => m.Id ).Name( TransactionHeaders.Id );
			this.Map( m => m.Date ).Name( TransactionHeaders.Date ).TypeConverterOption.Format( "ddd MMM dd yyyy HH:mm:ss \"GMT\"zzz" );
			this.Map( m => m.Destination ).Name( TransactionHeaders.Destination );
			this.Map( m => m.DestinationAmount ).Name( TransactionHeaders.DestinationAmount );
			this.Map( m => m.DestinationCurrency ).Name( TransactionHeaders.DestinationCurrency );
			this.Map( m => m.FeeAmount ).Name( TransactionHeaders.FeeAmount );
			this.Map( m => m.FeeCurrency ).Name( TransactionHeaders.FeeCurrency );
			this.Map( m => m.Origin ).Name( TransactionHeaders.Origin );
			this.Map( m => m.OriginAmount ).Name( TransactionHeaders.OriginAmount );
			this.Map( m => m.OriginCurrency ).Name( TransactionHeaders.OriginCurrency );
			this.Map( m => m.Status ).Name( TransactionHeaders.Status );
			this.Map( m => m.Type ).Name( TransactionHeaders.Type );
		}
	}
}