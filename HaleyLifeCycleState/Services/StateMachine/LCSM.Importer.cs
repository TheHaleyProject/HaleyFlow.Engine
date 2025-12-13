using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        public async Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromFileAsync(string filePath,string environmentName = "default", int envCode = 0) {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Definition file not found.", filePath);
            var json = await File.ReadAllTextAsync(filePath);
            return await ImportDefinitionFromJsonAsync(json, environmentName,envCode);
        }

        public async Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromJsonAsync(string json, string environmentName = "default", int envCode = 0) {
            var fb = new Feedback<DefinitionLoadResult>();

            try {
                if (string.IsNullOrWhiteSpace(json)) return fb.SetMessage("JSON is empty.");

                var spec = JsonSerializer.Deserialize<LifeCycleDefinitionJson>(json, JsonOptions);
                if (spec == null) return fb.SetMessage("Failed to deserialize JSON into LifeCycleDefinitionJson.");

                NormalizeDefinitionJson(spec);

                var defDisplay = spec.Definition.Name;
                var description = spec.Definition.Description ?? string.Empty;

                spec.Definition.Environment = string.IsNullOrWhiteSpace(environmentName) ? "default" : environmentName.Trim();
                spec.Definition.EnvironmentCode = envCode;

                var envFb = await Repository.UpsertEnvironment(spec.Definition.Environment, envCode);
                EnsureSuccess(envFb, "Environment_Upsert");
                var envId = envFb.Result!.GetInt("id");

                var defFb = await Repository.UpsertDefinition(defDisplay, description, envId);
                EnsureSuccess(defFb, "Definition_Upsert");
                var defId = defFb.Result!.GetLong("id");

                var versionString = spec.Definition.Version;
                if (!int.TryParse(versionString, out var versionNumber)) versionNumber = 1;

                var verUpFb = await Repository.UpsertDefinitionVersion(defId, versionNumber, json);
                EnsureSuccess(verUpFb, "DefVersion_Upsert");

                var latestFb = await Repository.GetLatestDefinitionVersion(new LifeCycleKey(LifeCycleKeyType.Id, defId));
                EnsureSuccess(latestFb, "DefVersion_GetLatest");
                var defVersionId = (int)latestFb.Result!.GetLong("id");

                // Categories (cache)
                var categoryByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                int ResolveCategoryId(string? category) {
                    var name = string.IsNullOrWhiteSpace(category) ? "business" : category.Trim();
                    if (categoryByName.TryGetValue(name, out var id)) return id;
                    var cfb = Repository.UpsertCategory(name).GetAwaiter().GetResult();
                    EnsureSuccess(cfb, "Category_Upsert");
                    var cid = cfb.Result!.GetInt("id");
                    categoryByName[name] = cid;
                    return cid;
                }

                // Events first (needed for state timeout_event mapping)
                var eventIdByCode = new Dictionary<int, int>();
                foreach (var e in spec.Events) {
                    var display = string.IsNullOrWhiteSpace(e.DisplayName) ? e.Name : e.DisplayName;
                    var code = e.Code > 0 ? e.Code : 0;
                    var evFb = await Repository.UpsertEvent(display!, code, defVersionId);
                    EnsureSuccess(evFb, "Event_Upsert");
                    eventIdByCode[code] = evFb.Result!.GetInt("id");
                }

                // States
                var stateIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in spec.States) {
                    var flags = BuildStateFlags(s);
                    var categoryId = ResolveCategoryId(s.Category);

                    int? timeoutMinutes = null;
                    if (!string.IsNullOrWhiteSpace(s.Timeout)) {
                        if (!ISODurationUtils.TryToMinutes(s.Timeout, out var mins, roundUp: true))
                            throw new InvalidOperationException($"Invalid ISO timeout '{s.Timeout}' for state '{s.Name}'.");
                        timeoutMinutes = mins;
                    }

                    var timeoutMode = 0;
                    if (!string.IsNullOrWhiteSpace(s.TimeoutMode)) {
                        var tm = s.TimeoutMode.Trim();
                        timeoutMode = tm.Equals("repeat", StringComparison.OrdinalIgnoreCase) ? 1 :
                                      tm.Equals("once", StringComparison.OrdinalIgnoreCase) ? 0 :
                                      throw new InvalidOperationException($"Invalid TimeoutMode '{s.TimeoutMode}' for state '{s.Name}'.");
                    }

                    var timeoutEventId = 0;
                    if (timeoutMinutes.HasValue && s.TimeoutEvent.HasValue && s.TimeoutEvent.Value > 0) {
                        if (!eventIdByCode.TryGetValue(s.TimeoutEvent.Value, out timeoutEventId))
                            throw new InvalidOperationException($"Unknown TimeoutEvent code '{s.TimeoutEvent}' for state '{s.Name}'.");
                    }

                    if (!timeoutMinutes.HasValue) { timeoutMode = 0; timeoutEventId = 0; }

                    var stFb = await Repository.UpsertState(s.Name, defVersionId, flags, categoryId, timeoutMinutes, timeoutMode, timeoutEventId);
                    EnsureSuccess(stFb, "State_Upsert");
                    stateIdByName[s.Name] = stFb.Result!.GetInt("id");
                }

                // Transitions
                foreach (var t in spec.Transitions) {
                    if (!stateIdByName.TryGetValue(t.From, out var fromStateId)) throw new InvalidOperationException($"Unknown state in transition 'from': '{t.From}'.");
                    if (!stateIdByName.TryGetValue(t.To, out var toStateId)) throw new InvalidOperationException($"Unknown state in transition 'to': '{t.To}'.");
                    if (!eventIdByCode.TryGetValue(t.Event, out var eventId)) throw new InvalidOperationException($"Unknown event code '{t.Event}' in transition.");

                    var trFb = await Repository.UpsertTransition(fromStateId, toStateId, eventId, defVersionId);
                    EnsureSuccess(trFb, "Transition_Upsert");
                }

                fb.SetStatus(true).SetResult(new DefinitionLoadResult {
                    DefinitionId = defId,
                    DefinitionVersionId = defVersionId,
                    StateCount = spec.States.Count,
                    EventCount = spec.Events.Count,
                    TransitionCount = spec.Transitions.Count
                });
            } catch (Exception ex) {
                fb.SetMessage(ex.Message).SetTrace(ex.StackTrace);
                if (ThrowExceptions || Repository.ThrowExceptions) throw;
            }

            return fb;
        }
    }
}
