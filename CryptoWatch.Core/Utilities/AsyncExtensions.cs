using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CryptoWatch.Core.Utilities {
	public static class AsyncExtensions {
		/// <summary>
		/// Allows a cancellation token to be awaited.
		/// </summary>
		[EditorBrowsable( EditorBrowsableState.Never )]
		public static CancellationTokenAwaiter GetAwaiter( this CancellationToken ct ) {
			// return our special awaiter
			return new CancellationTokenAwaiter {
				CancellationToken = ct
			};
		}

		/// <summary>
		/// The awaiter for cancellation tokens.
		/// </summary>
		[EditorBrowsable( EditorBrowsableState.Never )]
		public struct CancellationTokenAwaiter : ICriticalNotifyCompletion {
			public CancellationTokenAwaiter( CancellationToken cancellationToken ) {
				this.CancellationToken = cancellationToken;
			}

			internal CancellationToken CancellationToken;

			public object GetResult( ) {
				return null;
			}

			// called by compiler generated/.net internals to check
			// if the task has completed.
			public bool IsCompleted => this.CancellationToken.IsCancellationRequested;

			// The compiler will generate stuff that hooks in
			// here. We hook those methods directly into the
			// cancellation token.
			public void OnCompleted( Action continuation ) =>
				this.CancellationToken.Register( continuation );

			public void UnsafeOnCompleted( Action continuation ) =>
				this.CancellationToken.Register( continuation );
		}
	}
}