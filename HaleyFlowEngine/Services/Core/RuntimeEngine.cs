using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using static Haley.Internal.KeyConstants;

namespace Haley.Services {
    //RuntimeEngine is very dumb.. It only upserts the data without validating anything into the database. The results are always fetched via TimeLine
    internal sealed class RuntimeEngine : IRuntimeEngine {
        private readonly IWorkFlowDAL _dal;
        public RuntimeEngine(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }

        // All status updates go through this upsert. Call once at start with status="running",
        // call again at end with the final status — ON DUPLICATE KEY UPDATE handles the in-place update.
        public async Task<long> UpsertAsync(RuntimeLogByIdRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;

            try {
                var instanceId = await _dal.Instance.GetIdByGuidAsync(req.InstanceGuid, load);
                if (!instanceId.HasValue || instanceId.Value <= 0) throw new InvalidOperationException($"Instance not found: {req.InstanceGuid}");

                // frozen=false, lcId=0 — not used via public API; columns kept in DB for schema compat.
                var runtimeId = await _dal.Runtime.UpsertByKeyReturnIdAsync(instanceId.Value, req.ActivityId, req.StateId, req.ActorId, req.StatusId, 0, false, load);

                var dataJson    = req.Data    == null ? null : JsonSerializer.Serialize(req.Data);
                var payloadJson = req.Payload == null ? null : JsonSerializer.Serialize(req.Payload);
                await _dal.RuntimeData.UpsertAsync(runtimeId, dataJson, payloadJson, load);

                tx.Commit();
                committed = true;
                return runtimeId;
            } catch {
                if (!committed) tx.Rollback();
                throw;
            }
        }

        public async Task<long> EnsureActivityAsync(string displayName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var load = new DbExecutionLoad(ct);
            var row = await _dal.Activity.GetByNameAsync(displayName, load);
            return row != null ? row.GetLong(KEY_ID) : await _dal.Activity.InsertAsync(displayName, load);
        }

        public async Task<long> EnsureActivityStatusAsync(string displayName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var load = new DbExecutionLoad(ct);
            var row = await _dal.ActivityStatus.GetByNameAsync(displayName, load);
            return row != null ? row.GetLong(KEY_ID) : await _dal.ActivityStatus.InsertAsync(displayName, load);
        }
    }
}

