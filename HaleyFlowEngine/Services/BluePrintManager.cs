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
using static Haley.Internal.KeyConstants;

namespace Haley.Services {
    // BlueprintManager is the read cache for the entire structural/routing part of the engine.
    // It holds three in-memory caches:
    //   _latestDefVersion      — latest def_version row for each (envCode, defName) pair
    //   _blueprintsByVer       — fully hydrated LifeCycleBlueprint per def_version_id
    //   _consumerIdByEnvGuid   — numeric consumer DB id per (envCode, consumerGuid) pair
    //
    // All caches use ConcurrentDictionary<key, Lazy<Task<T>>> — the Lazy wraps the async fetch so
    // only ONE DB call fires per key even under concurrent startup requests (lazy singleton pattern).
    // On any DB error the faulted task is evicted from the cache (see AwaitCachedAsync) so the next
    // caller retries — preventing stale "always-faulted" entries.
    internal sealed class BlueprintManager : IBlueprintManager {
        private readonly IWorkFlowDAL _dal;
        private readonly ConcurrentDictionary<string, Lazy<Task<DbRow>>> _latestDefVersion = new();
        private readonly ConcurrentDictionary<long, Lazy<Task<LifeCycleBlueprint>>> _blueprintsByVer = new();
        private readonly ConcurrentDictionary<string, Lazy<Task<long>>> _consumerIdByEnvGuid = new();

        private static string NormalizeGuid(string guid) => (guid ?? string.Empty).Trim().ToLowerInvariant();

        public async Task<int> EnsureEnvironmentAsync(int envCode,string? envDisplayName, DbExecutionLoad load) {
            load.Ct.ThrowIfCancellationRequested();
            // you don’t have envDisplayName at runtime; keep it simple/consistent
            return await _dal.BlueprintWrite.EnsureEnvironmentByCodeAsync(envCode, envDisplayName?? envCode.ToString(), load);
        }

        public BlueprintManager(IWorkFlowDAL dal) { _dal = dal ?? throw new ArgumentNullException(nameof(dal)); }

        // Returns the latest def_version row for a (envCode, defName) pair, from cache.
        // "Latest" = highest version number for that definition in that environment.
        // The cache key is "envCode:normalizedDefName" — envCode scoping is critical because the
        // same definition name can exist across multiple environments independently.
        // After import (definition JSON changed), call Invalidate(envCode, defName) to force a reload.
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
            return await GetBlueprintByVersionIdAsync(dv.GetLong(KEY_ID), ct);
        }

        // Returns the fully-hydrated blueprint for a specific def_version_id, from cache.
        // Building the blueprint requires loading all states, events, and transitions for that version.
        // Blueprints are immutable once built — versions never change after creation — so caching forever
        // (until Invalidate is called) is correct and safe.
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

        // Loads all states, events, and transitions for the given def_version and assembles them into
        // a LifeCycleBlueprint — the engine's in-memory representation of the workflow graph.
        //
        // The blueprint is a set of lookup dictionaries for O(1) access at trigger time:
        //   StatesById      — id → StateDef
        //   EventsById      — id → EventDef
        //   EventsByName    — normalised name → EventDef   (for string event resolution)
        //   EventsByCode    — code (int) → EventDef        (for code-based resolution)
        //   Transitions     — (fromStateId, eventId) → TransitionDef
        //
        // The validator checks for exactly one initial state (IsInitial flag). Zero or multiple initial
        // states are programmer errors that should be caught early — hence the hard throws.
        private async Task<LifeCycleBlueprint> BuildBlueprintAsync(long defVersionId) {
            var dv = await _dal.Blueprint.GetDefVersionByIdAsync(defVersionId, DbExecutionLoad.None);
            if (dv == null) throw new InvalidOperationException($"def_version not found. id={defVersionId}");

            var bp = new LifeCycleBlueprint { DefVersionId = defVersionId, DefinitionId = dv.GetLong(KEY_PARENT), EnvCode = dv.GetInt(KEY_ENV_CODE), DefName = dv.GetString(KEY_DEF_NAME) ?? dv.GetString(KEY_NAME) ?? "unknown" };

            var stateRows = await _dal.Blueprint.ListStatesAsync(defVersionId, DbExecutionLoad.None);
            var eventRows = await _dal.Blueprint.ListEventsAsync(defVersionId, DbExecutionLoad.None);
            var transRows = await _dal.Blueprint.ListTransitionsAsync(defVersionId, DbExecutionLoad.None);

            var statesById = new Dictionary<long, StateDef>();
            long initialStateId = 0;

            foreach (var r in stateRows) {
                var flags = r.Get<uint>(KEY_FLAGS);
                var isInitial = (flags & (uint)LifeCycleStateFlag.IsInitial) != 0;
                var st = new StateDef { Id = r.GetInt(KEY_ID), Name = r.GetString(KEY_NAME) ?? "", DisplayName = r.GetString(KEY_DISPLAY_NAME) ?? r.GetString(KEY_NAME) ?? "", Flags = flags, IsInitial = isInitial };
                if (statesById.ContainsKey(st.Id)) throw new InvalidOperationException($"Duplicate state id in DB rows. id={st.Id}");
                statesById[st.Id] = st;
                if (isInitial) { if (initialStateId != 0) throw new InvalidOperationException($"Multiple initial states detected. defVersionId={defVersionId}"); initialStateId = st.Id; }
            }

            if (initialStateId == 0 && statesById.Count > 0) throw new InvalidOperationException($"No initial state defined for defVersionId={defVersionId}. Mark exactly one state with is_initial=true.");

            var eventsById = new Dictionary<long, EventDef>();
            var eventsByName = new Dictionary<string, EventDef>(StringComparer.OrdinalIgnoreCase);
            var eventsByCode = new Dictionary<int, EventDef>();

            foreach (var r in eventRows) {
                var ev = new EventDef { Id = r.GetInt(KEY_ID), Code = r.GetInt(KEY_CODE), Name = r.GetString(KEY_NAME) ?? "", DisplayName = r.GetString(KEY_DISPLAY_NAME) ?? r.GetString(KEY_NAME) ?? "" };
                if (eventsById.ContainsKey(ev.Id)) throw new InvalidOperationException($"Duplicate event id in DB rows. id={ev.Id}");
                eventsById[ev.Id] = ev;
                if (!string.IsNullOrWhiteSpace(ev.Name)) eventsByName[ev.Name.Trim().ToLowerInvariant()] = ev;
                if (eventsByCode.ContainsKey(ev.Code)) throw new InvalidOperationException($"Duplicate event code in DB rows. code={ev.Code}");
                eventsByCode[ev.Code] = ev;
            }

            var transitions = new Dictionary<(long fromStateId, int eventId), TransitionDef>();
            foreach (var r in transRows) {
                var fromId = r.GetLong(KEY_FROM_STATE);
                var toId = r.GetLong(KEY_TO_STATE);
                var evId = r.GetInt(KEY_EVENT);
                var key = (fromId, evId);
                if (transitions.ContainsKey(key)) throw new InvalidOperationException($"Duplicate transition detected. defVersionId={defVersionId}, from={fromId}, event={evId}");
                transitions[key] = new TransitionDef { FromStateId = fromId, ToStateId = toId, EventId = evId, Flags = r.Get<uint>(KEY_FLAGS) };
            }

            bp.StatesById = statesById;
            bp.EventsById = eventsById;
            bp.EventsByName = eventsByName;
            bp.EventsByCode = eventsByCode;
            bp.Transitions = transitions.ToDictionary(k => Tuple.Create(k.Key.fromStateId, k.Key.eventId), v => v.Value); // if your blueprint type requires Tuple keys
            bp.InitialStateId = initialStateId;

            return bp;
        }

        public Task<long> ResolveConsumerIdAsync(int envCode, string? consumerGuid, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            return EnsureConsumerIdAsync(envCode, consumerGuid!, ct);
        }

        // Registers (or re-registers) a consumer in the DB and returns its numeric ID.
        // Idempotent — safe to call every startup. The numeric ID is what gets stored in ack_consumer rows.
        // The result is cached in _consumerIdByEnvGuid so the lookup is free on every TriggerAsync call.
        // Note: if the consumer's envCode or guid changes between restarts, the cache clears naturally
        // on restart since caches are in-memory only.
        public Task<long> EnsureConsumerIdAsync(int envCode, string consumerGuid, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(consumerGuid)) throw new ArgumentNullException(nameof(consumerGuid));

            var key = $"{envCode}:{NormalizeGuid(consumerGuid)}";
            var lazy = _consumerIdByEnvGuid.GetOrAdd(key, _ => new Lazy<Task<long>>(() => EnsureConsumerIdCoreAsync(envCode, consumerGuid, ct)));
            return AwaitCachedAsync(_consumerIdByEnvGuid, key, lazy.Value, ct);
        }

        // Writes a heartbeat timestamp for the consumer. Called periodically (every N seconds) to signal
        // that the consumer process is alive. The monitor reads this timestamp to decide whether to
        // postpone resending events to a consumer (no point firing to an offline process).
        // Beat is NOT cached — every call goes to the DB to refresh the timestamp.
        public async Task BeatConsumerAsync(int envCode, string consumerGuid, CancellationToken ct = default) {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(consumerGuid)) throw new ArgumentNullException(nameof(consumerGuid));

            var tx0 = _dal.CreateNewTransaction();
            using var tx = tx0.Begin(false);
            var load = new DbExecutionLoad(ct, tx0);
            var committed = false;

            try {
                var envId = await EnsureEnvironmentAsync(envCode,null, load);
                await _dal.Consumer.UpsertBeatByEnvIdAndGuidAsync(envId, consumerGuid, load); // always refresh beat
                tx.Commit();
                committed = true;
            } finally {
                if (!committed) { try { tx.Rollback(); } catch { } }
            }
        }

        private async Task<long> EnsureConsumerIdCoreAsync(int envCode, string consumerGuid, CancellationToken ct) {
            var tx0 = _dal.CreateNewTransaction();
            using var tx = tx0.Begin(false);
            var load = new DbExecutionLoad(ct, tx0);
            var committed = false;

            try {
                var envId = await EnsureEnvironmentAsync(envCode, null,load);

                // ensure row exists -> id
                var id = await _dal.Consumer.EnsureByEnvIdAndGuidReturnIdAsync(envId, consumerGuid, load);

                // also “register/beat” on ensure (so RegisterConsumer = single call)
                await _dal.Consumer.UpsertBeatByEnvIdAndGuidAsync(envId, consumerGuid, load);

                tx.Commit();
                committed = true;
                return id;
            } finally {
                if (!committed) { try { tx.Rollback(); } catch { } }
            }
        }

        // Helper that awaits a cached Task while respecting cancellation.
        // Critical: if the task faults (DB error, timeout), we REMOVE it from the cache.
        // Without this, a faulted Lazy<Task<T>> would sit in the dict forever and every subsequent
        // caller would immediately get the same exception without a DB retry.
        private static async Task<T> AwaitCachedAsync<TKey, T>(ConcurrentDictionary<TKey, Lazy<Task<T>>> dict, TKey key, Task<T> task, CancellationToken ct) {
            try { return await task.WaitAsync(ct); } catch { dict.TryRemove(key, out _); throw; }
        }
    }
}


