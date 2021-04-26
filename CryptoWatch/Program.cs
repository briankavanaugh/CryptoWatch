﻿using System.Threading.Tasks;
using CryptoWatch.Core.Config;
using CryptoWatch.Entities.Contexts;
using CryptoWatch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Slack.Webhooks;

namespace CryptoWatch {
	static class Program {
		private static async Task Main( string[ ] args ) {
			using var host = AppStartup( args );
			await host.RunAsync( );
		}

		private static IHost AppStartup( string[ ] args ) =>
			Host.CreateDefaultBuilder( args )
			    .UseSerilog( )
			    .ConfigureAppConfiguration( ( context, configuration ) => {
				    configuration.Sources.Clear( );
				    var env = context.HostingEnvironment;
				    configuration
					    .AddJsonFile( "appSettings.json", true, true )
					    .AddJsonFile( $"appSettings.{env.EnvironmentName}", true, true )
					    .AddEnvironmentVariables( "Crypto_" );
				    var configurationRoot = configuration.Build( );
				    Log.Logger = new LoggerConfiguration( )
				                 .ReadFrom.Configuration( configurationRoot )
				                 .Enrich.FromLogContext( )
				                 .WriteTo.Console( )
				                 .CreateLogger( );

				    Log.Logger.Information( "Application Starting" );
			    } )
			    .ConfigureServices( ( context, services ) => {
				    // load configuration sections
				    var general = new GeneralConfiguration( );
					context.Configuration.Bind( "General", general );
					services.AddSingleton( general );

				    var cma = new CoinMarketCapConfiguration( );
				    context.Configuration.Bind( "CoinMarketCap", cma );
				    services.AddSingleton( cma );

				    var integrations = new IntegrationsConfiguration( );
				    context.Configuration.Bind( "Integrations", integrations );
				    services.AddSingleton( integrations );

				    var slack = new SlackClient( integrations.Slack );
				    services.AddSingleton( slack );

				    var contexts = new ConnectionStringsConfiguration( );
					context.Configuration.Bind( "ConnectionStrings", contexts );
					services.AddSingleton( contexts );

					// add database contexts
					services.AddDbContextPool<CryptoContext>( options => options.UseMySql( contexts.Crypto, ServerVersion.AutoDetect( contexts.Crypto ) ) );

				    // add services
				    services.AddSingleton<WatchService>( );
				    services.AddHostedService<CoinMarketCapService>( );
			    } )
			    .UseConsoleLifetime( )
			    .Build( );
	}
}