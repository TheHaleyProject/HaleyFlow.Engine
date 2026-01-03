using Haley.Abstractions;
using Haley.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Haley.Utils;
using System.Threading.Tasks;

namespace Haley.Services {
    internal sealed class BlueprintManager : IBlueprintManager {
        private readonly IWorkFlowDAL _dal;
        private readonly ConcurrentDictionary<string, Lazy<Task<DbRow>>> _latestDefVersion = new ConcurrentDictionary<string, Lazy<Task<DbRow>>>();
        private readonly ConcurrentDictionary<long, Lazy<Task<LifeCycleBlueprint>>> _byVersion = new ConcurrentDictionary<long, Lazy<Task<LifeCycleBlueprint>>>();

        public BlueprintManager(IWorkFlowDAL dal) { _dal = dal; }

        public async Task<DbRow> GetLatestDefVersionAsync(int envCode, string defName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var k = envCode + ":" + (defName ?? "").Trim().ToLowerInvariant();
            var lazy = _latestDefVersion.GetOrAdd(k, _ => new Lazy<Task<DbRow>>(async () => {
                var row = await _dal.Blueprint.GetLatestDefVersionByEnvCodeAndDefNameAsync(envCode, defName, default);
                if (row == null) throw new InvalidOperationException("def_version not found for envCode=" + envCode + ", defName=" + defName);
                return row;
            }));
            return await lazy.Value;
        }

        public async Task<DbRow> GetDefVersionByIdAsync(long defVersionId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var row = await _dal.Blueprint.GetDefVersionByIdAsync(defVersionId, default);
            if (row == null) throw new InvalidOperationException("def_version not found id=" + defVersionId);
            return row;
        }

        public async Task<LifeCycleBlueprint> GetBlueprintLatestAsync(int envCode, string defName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var dv = await GetLatestDefVersionAsync(envCode, defName, ct);
            return await GetBlueprintByVersionIdAsync(dv.GetLong("id"), ct);
        }

        public Task<LifeCycleBlueprint> GetBlueprintByVersionIdAsync(long defVersionId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var lazy = _byVersion.GetOrAdd(defVersionId, _ => new Lazy<Task<LifeCycleBlueprint>>(() => BuildBlueprintAsync(defVersionId, ct)));
            return lazy.Value;
        }

        public void Clear() { _latestDefVersion.Clear(); _byVersion.Clear(); }
        public void Invalidate(int envCode, string defName) { var k = envCode + ":" + (defName ?? "").Trim().ToLowerInvariant(); _latestDefVersion.TryRemove(k, out _); }
        public void Invalidate(long defVersionId) { _byVersion.TryRemove(defVersionId, out _); }

        private async Task<LifeCycleBlueprint> BuildBlueprintAsync(long defVersionId, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var dv = await _dal.Blueprint.GetDefVersionByIdAsync(defVersionId, default);
            if (dv == null) throw new InvalidOperationException("def_version not found id=" + defVersionId);

            var definitionId = dv.GetLong("parent");
            var envCode = dv.GetInt("env_code");
            var defName = dv.GetString("def_name") ?? dv.GetString("name") ?? "unknown";

            var statesRows = await _dal.Blueprint.ListStatesAsync(defVersionId, default);
            var eventsRows = await _dal.Blueprint.ListEventsAsync(defVersionId, default);
            var transRows = await _dal.Blueprint.ListTransitionsAsync(defVersionId, default);

            var statesById = new Dictionary<long, StateDef>();
            long initialStateId = 0;

            foreach (var r in statesRows) {
                var flags = r.Get<uint>("flags");
                var isInitial = (flags & 1u) == 1u;
                var st = new StateDef {
                    Id = r.GetLong("id"),
                    Name = r.GetString("name") ?? "",
                    DisplayName = r.GetString("display_name") ?? r.GetString("name") ?? "",
                    Flags = flags,
                    TimeoutMinutes = r.GetNullableInt("timeout_minutes"),
                    TimeoutEventId = r.GetNullableLong("timeout_event"),
                    IsInitial = isInitial
                };
                statesById[st.Id] = st;
                if (isInitial && initialStateId == 0) initialStateId = st.Id;
            }

            if (initialStateId == 0 && statesById.Count > 0) initialStateId = statesById.Values.First().Id;

            var eventsById = new Dictionary<long, EventDef>();
            var eventsByName = new Dictionary<string, EventDef>();
            var eventsByCode = new Dictionary<int, EventDef>();

            foreach (var r in eventsRows) {
                var ev = new EventDef {
                    Id = r.GetLong("id"),
                    Code = r.GetInt("code"),
                    Name = r.GetString("name") ?? "",
                    DisplayName = r.GetString("display_name") ?? r.GetString("name") ?? ""
                };
                eventsById[ev.Id] = ev;
                if (!string.IsNullOrWhiteSpace(ev.Name)) eventsByName[ev.Name.Trim().ToLowerInvariant()] = ev;
                eventsByCode[ev.Code] = ev;
            }

            var transitions = new Dictionary<Tuple<long, long>, TransitionDef>();
            foreach (var r in transRows) {
                var fromId = r.GetLong("from_state");
                var toId = r.GetLong("to_state");
                var evId = r.GetLong("event");
                transitions[Tuple.Create(fromId, evId)] = new TransitionDef { FromStateId = fromId, ToStateId = toId, EventId = evId, Flags = r.Get<uint>("flags") };
            }

            return new LifeCycleBlueprint {
                DefVersionId = defVersionId,
                DefinitionId = definitionId,
                EnvCode = envCode,
                DefName = defName,
                StatesById = statesById,
                EventsById = eventsById,
                EventsByName = eventsByName,
                EventsByCode = eventsByCode,
                Transitions = transitions,
                InitialStateId = initialStateId
            };
        }
    }

}
