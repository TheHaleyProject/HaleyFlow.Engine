using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        public async Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromFileAsync(string filePath) =>
            await ImportDefinitionFromJsonAsync(await File.ReadAllTextAsync(filePath));

        public async Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromJsonAsync(string json) {
            var fb = new Feedback<DefinitionLoadResult>();

            try {
                var spec = JsonSerializer.Deserialize<DefinitionJson>(json, _jsonOptions) ?? throw new InvalidOperationException("Invalid JSON.");
                NormalizeSpec(spec);

                int env = ResolveEnvironment(spec.Environment);

                // -------------------------
                // Definition
                // -------------------------
                long definitionId;

                var exists = await _repo.DefinitionExists(spec.Definition.Name, env);
                if (exists.Status && exists.Result) {
                    var defs = await _repo.GetAllDefinitions();
                    var match = defs.Status
                        ? defs.Result?.FirstOrDefault(d =>
                            string.Equals(GetStr(d, "display_name"), spec.Definition.Name, StringComparison.OrdinalIgnoreCase) &&
                            ToInt(d, "env") == env)
                        : null;

                    if (match is null) {
                        var defReg = await _repo.RegisterDefinition(spec.Definition.Name, spec.Definition.Description ?? "", env);
                        if (!defReg.Status || defReg.Result == null) return fb.SetMessage(defReg.Message);
                        definitionId = ToLong(defReg.Result, "id");
                    } else {
                        definitionId = ToLong(match, "id");
                    }
                } else {
                    var defReg = await _repo.RegisterDefinition(spec.Definition.Name, spec.Definition.Description ?? "", env);
                    if (!defReg.Status || defReg.Result == null) return fb.SetMessage(defReg.Message);
                    definitionId = ToLong(defReg.Result, "id");
                }

                // -------------------------
                // Definition Version
                // -------------------------
                int versionCode = ParseVersionInt(spec.Definition.Version, spec.Definition.VersionCode);

                var verReg = await _repo.RegisterDefinitionVersion(definitionId, versionCode, json);
                if (!verReg.Status || verReg.Result == null) return fb.SetMessage(verReg.Message);

                int defVersionId = ToInt(verReg.Result, "id");

                // -------------------------
                // Categories
                // -------------------------
                var categoryNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var catName in spec.States.Select(s => s.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase)) {
                    var catGet = await _repo.GetCategoryByNameAsync(catName!);
                    if (catGet.Status && catGet.Result != null) {
                        categoryNameToId[catName!] = ToInt(catGet.Result, "id");
                    } else {
                        var catIns = await _repo.InsertCategoryAsync(catName!);
                        if (!catIns.Status || catIns.Result == null) return fb.SetMessage(catIns.Message);
                        categoryNameToId[catName!] = ToInt(catIns.Result, "id");
                    }
                }

                // -------------------------
                // States
                // -------------------------
                var stateNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var s in spec.States) {
                    var flags = BuildStateFlags(s);
                    int categoryId = (s.Category != null && categoryNameToId.TryGetValue(s.Category, out var cid)) ? cid : 0;

                    var get = await _repo.GetStateByName(defVersionId, s.Name);
                    int stateId;

                    if (get.Status && get.Result != null) {
                        stateId = ToInt(get.Result, "id");
                    } else {
                        var ins = await _repo.RegisterState(s.Name, defVersionId, flags, categoryId);
                        if (!ins.Status || ins.Result == null) return fb.SetMessage(ins.Message);
                        stateId = ToInt(ins.Result, "id");
                    }

                    stateNameToId[s.Name] = stateId;
                }

                // -------------------------
                // Events (union)
                // -------------------------
                var eventNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (spec.Events != null) {
                    foreach (var e in spec.Events.Where(e => !string.IsNullOrWhiteSpace(e)))
                        eventNames.Add(e!);
                }

                foreach (var t in spec.Transitions) {
                    if (!string.IsNullOrWhiteSpace(t.Event))
                        eventNames.Add(t.Event!);
                }

                var eventNameToId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var ev in eventNames) {
                    var existing = await _repo.GetEventByName(defVersionId, ev);
                    if (existing.Status && existing.Result != null) {
                        eventNameToId[ev] = ToInt(existing.Result, "id");
                    } else {
                        var ins = await _repo.RegisterEvent(ev, defVersionId);
                        if (!ins.Status || ins.Result == null) return fb.SetMessage(ins.Message);
                        eventNameToId[ev] = ToInt(ins.Result, "id");
                    }
                }

                // -------------------------
                // Transitions
                // -------------------------
                var createdTransitions = 0;

                foreach (var t in spec.Transitions) {
                    if (!stateNameToId.TryGetValue(t.From, out var fromId))
                        return fb.SetMessage($"Transition 'from' state '{t.From}' not found.");

                    if (!stateNameToId.TryGetValue(t.To, out var toId))
                        return fb.SetMessage($"Transition 'to' state '{t.To}' not found.");

                    if (string.IsNullOrWhiteSpace(t.Event))
                        return fb.SetMessage($"Transition from '{t.From}' is missing 'event'.");

                    if (!eventNameToId.TryGetValue(t.Event!, out var evId))
                        return fb.SetMessage($"Transition event '{t.Event}' not found.");

                    var flags = BuildTransitionFlags(t);

                    var existing = await _repo.GetTransition(fromId, evId, defVersionId);
                    if (existing.Status && existing.Result != null) continue;

                    var ins = await _repo.RegisterTransition(fromId, toId, evId, defVersionId, flags, t.Guard ?? "");
                    if (!ins.Status) return fb.SetMessage(ins.Message);

                    createdTransitions++;
                }

                return fb.SetStatus(true).SetResult(new DefinitionLoadResult {
                    DefinitionId = definitionId,
                    DefinitionVersionId = defVersionId,
                    Environment = spec.Environment!,
                    VersionCode = versionCode,
                    StateCount = stateNameToId.Count,
                    EventCount = eventNameToId.Count,
                    TransitionCount = createdTransitions
                });

            } catch (Exception ex) {
                if (_repo.ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
