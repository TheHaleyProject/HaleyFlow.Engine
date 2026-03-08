using Haley.Models;

namespace Haley.Abstractions {
    internal interface IEngineCareDAL {
        Task<long> CountConsumersTotalAsync(DbExecutionLoad load = default);
        Task<long> CountConsumersAliveAsync(int ttlSeconds, DbExecutionLoad load = default);
        Task<long> CountConsumersDownAsync(int ttlSeconds, DbExecutionLoad load = default);
        Task<long> CountStaleDefaultStateAsync(uint excludedFlags, int ackStatus, int staleSeconds, DbExecutionLoad load = default);
    }
}
