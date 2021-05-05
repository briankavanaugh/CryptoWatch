using System.Threading;
using System.Threading.Tasks;

namespace CryptoWatch.Services {
    public interface IPriceService {
        Task UpdateBalances( CancellationToken cancellationToken );
    }
}