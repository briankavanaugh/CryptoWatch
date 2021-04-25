using Microsoft.EntityFrameworkCore;
using CryptoWatch.Entities.Domains;
using Pomelo.EntityFrameworkCore.MySql.Extensions;

#nullable disable

namespace CryptoWatch.Entities.Contexts {
	public partial class CryptoContext : DbContext {

		public CryptoContext( DbContextOptions<CryptoContext> options )
			: base( options ) { }

		public virtual DbSet<Balance> Balances { get; set; }
		public virtual DbSet<CryptoCurrency> CryptoCurrencies { get; set; }
		public virtual DbSet<Transaction> Transactions { get; set; }

		protected override void OnModelCreating( ModelBuilder modelBuilder ) {
			modelBuilder.HasCharSet( "utf8" )
			            .UseCollation( "utf8_general_ci" );

			modelBuilder.Entity<Balance>( entity => {
				entity.HasNoKey( );

				entity.ToView( "Balance" );

				entity.Property( e => e.AltSymbol )
				      .HasMaxLength( 10 )
				      .HasComment( "Symbol used on CoinMarketApp, when different" );

				entity.Property( e => e.Amount ).HasPrecision( 45, 18 );

				entity.Property( e => e.Exclude )
				      .HasColumnType( "bit(1)" )
				      .HasComment( "When true, exclude from quote requests" );

				entity.Property( e => e.Id ).HasColumnType( "int(11)" );

				entity.Property( e => e.Name ).HasMaxLength( 50 );

				entity.Property( e => e.Symbol )
				      .IsRequired( )
				      .HasMaxLength( 10 );
			} );

			modelBuilder.Entity<CryptoCurrency>( entity => {
				entity.ToTable( "CryptoCurrency" );

				entity.HasIndex( e => e.Symbol, "Symbol" );

				entity.Property( e => e.Id ).HasColumnType( "int(11)" );

				entity.Property( e => e.AddedToExchange ).HasColumnType( "date" );

				entity.Property( e => e.AltSymbol )
				      .HasMaxLength( 10 )
				      .HasComment( "Symbol used on CoinMarketApp, when different" );

				entity.Property( e => e.Created )
				      .HasColumnType( "timestamp" )
				      .HasDefaultValueSql( "current_timestamp()" );

				entity.Property( e => e.Exclude )
				      .HasColumnType( "bit(1)" )
				      .HasDefaultValueSql( "b'0'" )
				      .HasComment( "When true, exclude from quote requests" );

				entity.Property( e => e.ExternalId ).HasColumnType( "bigint(20)" );

				entity.Property( e => e.Name ).HasMaxLength( 50 );

				entity.Property( e => e.Slug ).HasMaxLength( 50 );

				entity.Property( e => e.Symbol )
				      .IsRequired( )
				      .HasMaxLength( 10 );
			} );

			modelBuilder.Entity<Transaction>( entity => {
				entity.ToTable( "Transaction" );

				entity.HasIndex( e => e.CryptoCurrencyId, "FK_Transaction_CryptoCurrency" );

				entity.Property( e => e.Id ).HasColumnType( "int(11)" );

				entity.Property( e => e.Amount )
				      .HasPrecision( 23, 18 )
				      .HasComment( "Buy/transfer in is positive, sell/transfer out is negative" );

				entity.Property( e => e.Created )
				      .HasColumnType( "timestamp" )
				      .HasDefaultValueSql( "current_timestamp()" );

				entity.Property( e => e.CryptoCurrencyId ).HasColumnType( "int(11)" );

				entity.Property( e => e.Destination )
				      .IsRequired( )
				      .HasMaxLength( 15 )
				      .HasDefaultValueSql( "'uphold'" );

				entity.Property( e => e.ExternalId ).HasComment( "UUID" );

				entity.Property( e => e.Origin )
				      .IsRequired( )
				      .HasMaxLength( 15 );

				entity.Property( e => e.Status )
				      .IsRequired( )
				      .HasMaxLength( 15 );

				entity.Property( e => e.TransactionDate ).HasColumnType( "datetime" );

				entity.Property( e => e.Type )
				      .IsRequired( )
				      .HasMaxLength( 15 );

				entity.HasOne( d => d.CryptoCurrency )
				      .WithMany( p => p.Transactions )
				      .HasForeignKey( d => d.CryptoCurrencyId )
				      .OnDelete( DeleteBehavior.ClientSetNull )
				      .HasConstraintName( "FK_Transaction_CryptoCurrency" );
			} );

			OnModelCreatingPartial( modelBuilder );
		}

		partial void OnModelCreatingPartial( ModelBuilder modelBuilder );
	}
}