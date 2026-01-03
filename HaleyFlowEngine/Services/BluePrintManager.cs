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
        private readonly ConcurrentDictionary<string, Lazy<Task<DbRow>>> _latestDefVersion = new();
        private readonly ConcurrentDictionary<long, Lazy<Task<LifeCycleBlueprint>>> _blueprintsByVer = new();

        public BlueprintManager(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }

        public Task<DbRow> GetLatestDefVersionAsync(int envCode, string defName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var key = $"{envCode}:{(defName ?? string.Empty).Trim().ToLowerInvariant()}";
            var lazy = _latestDefVersion.GetOrAdd(key, _ => new Lazy<Task<DbRow>>(() => LoadLatestDefVersionAsync(envCode, defName, ct)));
            return lazy.Value;
        }

        public async Task<DbRow> GetDefVersionByIdAsync(long defVersionId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var row = await _dal.Blueprint.GetDefVersionByIdAsync(defVersionId, default);
            if (row == null) throw new InvalidOperationException($"def_version not found. id={defVersionId}");
            return row;
        }

        public async Task<LifeCycleBlueprint> GetBlueprintLatestAsync(int envCode, string defName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var dv = await GetLatestDefVersionAsync(envCode, defName, ct);
            return await GetBlueprintByVersionIdAsync(dv.GetLong("id"), ct);
        }

        public Task<LifeCycleBlueprint> GetBlueprintByVersionIdAsync(long defVersionId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var lazy = _blueprintsByVer.GetOrAdd(defVersionId, _ => new Lazy<Task<LifeCycleBlueprint>>(() => BuildBlueprintAsync(defVersionId, ct)));
            return lazy.Value;
        }

        public void Clear() { _latestDefVersion.Clear(); _blueprintsByVer.Clear(); }

        public void Invalidate(int envCode, string defName) { _latestDefVersion.TryRemove($"{envCode}:{(defName ?? string.Empty).Trim().ToLowerInvariant()}", out _); }

        public void Invalidate(long defVersionId) { _blueprintsByVer.TryRemove(defVersionId, out _); }

        private async Task<DbRow> LoadLatestDefVersionAsync(int envCode, string defName, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();
            var row = await _dal.Blueprint.GetLatestDefVersionByEnvCodeAndDefNameAsync(envCode, defName, default);
            if (row == null) throw new InvalidOperationException($"def_version not found. env={envCode}, def={defName}");
            return row;
        }

        private async Task<LifeCycleBlueprint> BuildBlueprintAsync(long defVersionId, CancellationToken ct) {
            ct.ThrowIfCancellationRequested();

            var dv = await _dal.Blueprint.GetDefVersionByIdAsync(defVersionId, default);
            if (dv == null) throw new InvalidOperationException($"def_version not found. id={defVersionId}");

            var bp = new LifeCycleBlueprint();
            bp.DefVersionId = defVersionId;
            bp.DefinitionId = dv.GetLong("parent");
            bp.EnvCode = dv.GetInt("env_code");
            bp.DefName = dv.GetString("def_name") ?? dv.GetString("name") ?? "unknown";

            var stateRows = await _dal.Blueprint.ListStatesAsync(defVersionId, default);
            var eventRows = await _dal.Blueprint.ListEventsAsync(defVersionId, default);
            var transRows = await _dal.Blueprint.ListTransitionsAsync(defVersionId, default);

            var statesById = new Dictionary<long, StateDef>();
            long initialStateId = 0;

            foreach (var r in stateRows) {
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

            foreach (var r in eventRows) {
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

            bp.StatesById = statesById;
            bp.EventsById = eventsById;
            bp.EventsByName = eventsByName;
            bp.EventsByCode = eventsByCode;
            bp.Transitions = transitions;
            bp.InitialStateId = initialStateId;

            return bp;
        }
    }
}
