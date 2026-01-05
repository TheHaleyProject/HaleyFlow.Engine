using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Services {
    internal sealed class BlueprintManager : IBlueprintManager {
        private readonly IWorkFlowDAL _dal;
        private readonly ConcurrentDictionary<string, Lazy<Task<DbRow>>> _latestDefVersion = new();
        private readonly ConcurrentDictionary<long, Lazy<Task<LifeCycleBlueprint>>> _blueprintsByVer = new();

        public BlueprintManager(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }

        public Task<DbRow> GetLatestDefVersionAsync(int envCode, string defName, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var key = $"{envCode}:{(defName ?? string.Empty).N()}";
            var lazy = _latestDefVersion.GetOrAdd(key, _ => new Lazy<Task<DbRow>>(() => LoadLatestDefVersionAsync(envCode, defName)));
            return AwaitCachedAsync(_latestDefVersion, key, lazy.Value, ct);
        }

        public async Task<DbRow> GetDefVersionByIdAsync(long defVersionId, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            var row = await _dal.Blueprint.GetDefVersionByIdAsync(defVersionId, DbExecutionLoad.None);
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
            var lazy = _blueprintsByVer.GetOrAdd(defVersionId, _ => new Lazy<Task<LifeCycleBlueprint>>(() => BuildBlueprintAsync(defVersionId)));
            return AwaitCachedAsync(_blueprintsByVer, defVersionId, lazy.Value, ct);
        }

        public void Clear() { _latestDefVersion.Clear(); _blueprintsByVer.Clear(); }

        public void Invalidate(int envCode, string defName) { _latestDefVersion.TryRemove($"{envCode}:{(defName ?? string.Empty).N()}", out _); }

        public void Invalidate(long defVersionId) { _blueprintsByVer.TryRemove(defVersionId, out _); }

        private async Task<DbRow> LoadLatestDefVersionAsync(int envCode, string defName) {
            var row = await _dal.Blueprint.GetLatestDefVersionByEnvCodeAndDefNameAsync(envCode, defName, DbExecutionLoad.None);
            if (row == null) throw new InvalidOperationException($"def_version not found. env={envCode}, def={defName}");
            return row;
        }

        private async Task<LifeCycleBlueprint> BuildBlueprintAsync(long defVersionId) {
            var dv = await _dal.Blueprint.GetDefVersionByIdAsync(defVersionId, DbExecutionLoad.None);
            if (dv == null) throw new InvalidOperationException($"def_version not found. id={defVersionId}");

            var bp = new LifeCycleBlueprint { DefVersionId = defVersionId, DefinitionId = dv.GetLong("parent"), EnvCode = dv.GetInt("env_code"), DefName = dv.GetString("def_name") ?? dv.GetString("name") ?? "unknown" };

            var stateRows = await _dal.Blueprint.ListStatesAsync(defVersionId, DbExecutionLoad.None);
            var eventRows = await _dal.Blueprint.ListEventsAsync(defVersionId, DbExecutionLoad.None);
            var transRows = await _dal.Blueprint.ListTransitionsAsync(defVersionId, DbExecutionLoad.None);

            var statesById = new Dictionary<long, StateDef>();
            long initialStateId = 0;

            foreach (var r in stateRows) {
                var flags = r.Get<uint>("flags");
                var isInitial = (flags & (uint)LifeCycleStateFlag.IsInitial) != 0;
                var st = new StateDef { Id = r.GetInt("id"), Name = r.GetString("name") ?? "", DisplayName = r.GetString("display_name") ?? r.GetString("name") ?? "", Flags = flags, TimeoutMinutes = r.GetNullableInt("timeout_minutes"), TimeoutEventId = r.GetNullableLong("timeout_event"), IsInitial = isInitial };
                if (statesById.ContainsKey(st.Id)) throw new InvalidOperationException($"Duplicate state id in DB rows. id={st.Id}");
                statesById[st.Id] = st;
                if (isInitial) { if (initialStateId != 0) throw new InvalidOperationException($"Multiple initial states detected. defVersionId={defVersionId}"); initialStateId = st.Id; }
            }

            if (initialStateId == 0 && statesById.Count > 0) initialStateId = statesById.Values.First().Id; // keep fallback if you want; otherwise throw

            var eventsById = new Dictionary<long, EventDef>();
            var eventsByName = new Dictionary<string, EventDef>(StringComparer.OrdinalIgnoreCase);
            var eventsByCode = new Dictionary<int, EventDef>();

            foreach (var r in eventRows) {
                var ev = new EventDef { Id = r.GetInt("id"), Code = r.GetInt("code"), Name = r.GetString("name") ?? "", DisplayName = r.GetString("display_name") ?? r.GetString("name") ?? "" };
                if (eventsById.ContainsKey(ev.Id)) throw new InvalidOperationException($"Duplicate event id in DB rows. id={ev.Id}");
                eventsById[ev.Id] = ev;
                if (!string.IsNullOrWhiteSpace(ev.Name)) eventsByName[ev.Name.Trim().ToLowerInvariant()] = ev;
                if (eventsByCode.ContainsKey(ev.Code)) throw new InvalidOperationException($"Duplicate event code in DB rows. code={ev.Code}");
                eventsByCode[ev.Code] = ev;
            }

            var transitions = new Dictionary<(long fromStateId, int eventId), TransitionDef>();
            foreach (var r in transRows) {
                var fromId = r.GetLong("from_state");
                var toId = r.GetLong("to_state");
                var evId = r.GetInt("event");
                var key = (fromId, evId);
                if (transitions.ContainsKey(key)) throw new InvalidOperationException($"Duplicate transition detected. defVersionId={defVersionId}, from={fromId}, event={evId}");
                transitions[key] = new TransitionDef { FromStateId = fromId, ToStateId = toId, EventId = evId, Flags = r.Get<uint>("flags") };
            }

            bp.StatesById = statesById;
            bp.EventsById = eventsById;
            bp.EventsByName = eventsByName;
            bp.EventsByCode = eventsByCode;
            bp.Transitions = transitions.ToDictionary(k => Tuple.Create(k.Key.fromStateId, k.Key.eventId), v => v.Value); // if your blueprint type requires Tuple keys
            bp.InitialStateId = initialStateId;

            return bp;
        }

        private static async Task<T> AwaitCachedAsync<TKey, T>(ConcurrentDictionary<TKey, Lazy<Task<T>>> dict, TKey key, Task<T> task, CancellationToken ct) {
            try { return await task.WaitAsync(ct); } catch { dict.TryRemove(key, out _); throw; }
        }
    }

}
