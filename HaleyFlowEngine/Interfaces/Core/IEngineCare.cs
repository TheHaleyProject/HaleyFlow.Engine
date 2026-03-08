using Haley.Models;

namespace Haley.Abstractions {
    // EngineCare owns the health and maintenance query surface for the engine.
    // It is analogous to AckManager (owns ACK lifecycle) but for operational diagnostics:
    // consumer liveness counts, stale-instance counts, and any future maintenance operations
    // (e.g., archiving, pruning). WorkFlowEngine.GetHealthAsync delegates entirely to this
    // class — all DB-bound counting (via IEngineCareDAL) and ACK counting (via IAckManager)
    // happens here, keeping WorkFlowEngine itself free of health-query logic.
    internal interface IEngineCare {
        Task<WorkFlowEngineHealth> GetHealthAsync(int ttlSeconds, TimeSpan defaultStateStaleDuration, CancellationToken ct = default);
    }
}
