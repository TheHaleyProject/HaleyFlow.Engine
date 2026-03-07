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
    internal sealed class RuntimeEngine : IRuntimeEngine {
        private readonly IWorkFlowDAL _dal;
        public RuntimeEngine(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }

        public async Task<long> UpsertAsync(RuntimeLogByIdRequest req, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            var transaction = _dal.CreateNewTransaction();
            using var tx = transaction.Begin(false);
            var load = new DbExecutionLoad(ct, transaction);
            var committed = false;

            try {
                var instanceId = await _dal.Instance.GetIdByGuidAsync(req.InstanceGuid, load);
                if (!instanceId.HasValue || instanceId.Value <= 0) throw new InvalidOperationException($"Instance not found: {req.InstanceGuid}");

                var runtimeId = await _dal.Runtime.UpsertByKeyReturnIdAsync(instanceId.Value, req.ActivityId, req.StateId, req.ActorId, req.StatusId, req.LcId, req.Frozen, load);

                var dataJson = req.Data == null ? null : JsonSerializer.Serialize(req.Data);
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

        public async Task<int> SetStatusAsync(long runtimeId, string status, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (runtimeId <= 0) throw new ArgumentOutOfRangeException(nameof(runtimeId));
            if (string.IsNullOrWhiteSpace(status)) throw new ArgumentNullException(nameof(status));

            var statusId = await EnsureActivityStatusAsync(status, ct);
            return await SetStatusAsync(runtimeId, statusId, ct);
        }

        public Task<int> SetStatusAsync(long runtimeId, long statusId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            return _dal.Runtime.SetStatusAsync(runtimeId, statusId, new DbExecutionLoad(ct));
        }

        public Task<int> SetFrozenAsync(long runtimeId, bool frozen, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            return _dal.Runtime.SetFrozenAsync(runtimeId, frozen, new DbExecutionLoad(ct));
        }

        public Task<int> SetLcIdAsync(long runtimeId, long lcId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            return _dal.Runtime.SetLcIdAsync(runtimeId, lcId, new DbExecutionLoad(ct));
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

