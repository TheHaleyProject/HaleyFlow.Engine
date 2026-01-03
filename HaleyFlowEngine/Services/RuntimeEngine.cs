using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Services {
    internal sealed class RuntimeEngine : IRuntimeEngine {
        private readonly IWorkFlowDAL _dal;

        public RuntimeEngine(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }

        public async Task<long> UpsertAsync(RuntimeUpsertRequest req, DbExecutionLoad load = default, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();

            var inst = await _dal.Instance.GetByGuidAsync(req.InstanceGuid, load);
            if (inst == null) throw new InvalidOperationException($"Instance not found: {req.InstanceGuid}");

            var instanceId = inst.GetLong("id");

            var runtimeId = await _dal.Runtime.UpsertByKeyReturnIdAsync(instanceId, req.ActivityId, req.StateId, req.ActorId, req.StatusId, req.LcId, req.Frozen, load);

            var dataJson = req.Data == null ? null : JsonSerializer.Serialize(req.Data);
            var payloadJson = req.Payload == null ? null : JsonSerializer.Serialize(req.Payload);
            await _dal.RuntimeData.UpsertAsync(runtimeId, dataJson, payloadJson, load);

            return runtimeId;
        }

        public Task<int> SetStatusAsync(long runtimeId, long statusId, DbExecutionLoad load = default, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _dal.Runtime.SetStatusAsync(runtimeId, statusId, load); }

        public Task<int> SetFrozenAsync(long runtimeId, bool frozen, DbExecutionLoad load = default, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _dal.Runtime.SetFrozenAsync(runtimeId, frozen, load); }

        public Task<int> SetLcIdAsync(long runtimeId, long lcId, DbExecutionLoad load = default, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _dal.Runtime.SetLcIdAsync(runtimeId, lcId, load); }

        // Optional helpers (inside SAME class, per your request)
        public Task<DbRows> ListActivitiesAsync(DbExecutionLoad load = default, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _dal.Activity.ListAllAsync(load); }

        public Task<DbRow?> GetActivityByNameAsync(string name, DbExecutionLoad load = default, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _dal.Activity.GetByNameAsync(name, load); }

        public async Task<long> EnsureActivityAsync(string displayName, DbExecutionLoad load = default, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var row = await _dal.Activity.GetByNameAsync(displayName, load);
            return row != null ? row.GetLong("id") : await _dal.Activity.InsertAsync(displayName, load);
        }

        public Task<DbRows> ListActivityStatusesAsync(DbExecutionLoad load = default, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _dal.ActivityStatus.ListAllAsync(load); }

        public Task<DbRow?> GetActivityStatusByNameAsync(string name, DbExecutionLoad load = default, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return _dal.ActivityStatus.GetByNameAsync(name, load); }

        public async Task<long> EnsureActivityStatusAsync(string displayName, DbExecutionLoad load = default, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var row = await _dal.ActivityStatus.GetByNameAsync(displayName, load);
            return row != null ? row.GetLong("id") : await _dal.ActivityStatus.InsertAsync(displayName, load);
        }
    }
}