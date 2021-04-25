using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CryptoWatch.Services {
	public abstract class HostedService : IHostedService {
		#region Member Variables

		private Task executingTask;
		private CancellationTokenSource cts;

		#endregion

		#region Constructor

		protected HostedService( ILogger logger ) {
			this.Logger = logger;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Returns the name of the service (for logging purposes).
		/// </summary>
		protected abstract string ServiceName { get; }

		protected ILogger Logger { get; }

		#endregion

		#region Implementation of IHostedService

		/// <inheritdoc />
		public Task StartAsync( CancellationToken cancellationToken ) {
			this.Logger.LogInformation( $"Starting {this.ServiceName}." );
			// Create a linked token so we can trigger cancellation outside of this token's cancellation
			this.cts = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );

			// Store the task we're executing
			this.executingTask = this.ExecuteAsync( this.cts.Token );

			this.Logger.LogInformation( $"{this.ServiceName} started." );

			// If the task is completed then return it, otherwise it's running
			return this.executingTask.IsCompleted ? this.executingTask : Task.CompletedTask;
		}

		/// <inheritdoc />
		public async Task StopAsync( CancellationToken cancellationToken ) {
			this.Logger.LogInformation( $"Stopping {this.ServiceName}." );
			if( this.executingTask == null )
				return;

			// signal cancellation to the executing method
			this.cts.Cancel( );

			// wait until the task completes or the stop token triggers
			await Task.WhenAny( this.executingTask, Task.Delay( -1, cancellationToken ) );

			// throw if cancellation triggered
			cancellationToken.ThrowIfCancellationRequested( );
		}

		#endregion

		#region Methods

		/// <summary>
		/// Derived classes should implement this to start the long-running method.
		/// </summary>
		protected abstract Task ExecuteAsync( CancellationToken cancellationToken );

		#endregion

	}
}