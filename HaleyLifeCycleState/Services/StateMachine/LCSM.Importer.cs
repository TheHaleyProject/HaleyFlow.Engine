using Haley.Abstractions;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {
        public async Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromFileAsync(string filePath) {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath)) throw new FileNotFoundException("Definition file not found.", filePath);
            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            return await ImportDefinitionFromJsonAsync(json).ConfigureAwait(false);
        }

        public async Task<IFeedback<DefinitionLoadResult>> ImportDefinitionFromJsonAsync(string json) {
            var fb = new Feedback<DefinitionLoadResult>();

            try {
                if (string.IsNullOrWhiteSpace(json)) {
                    fb.SetMessage("JSON is empty.");
                    return fb;
                }

                var spec = JsonSerializer.Deserialize<LifeCycleDefinitionJson>(json, JsonOptions);
                if (spec == null) {
                    fb.SetMessage("Failed to deserialize JSON into LifeCycleDefinitionJson.");
                    return fb;
                }

                NormalizeDefinitionJson(spec);

                var env = 0;
                var displayName = spec.Definition.Name;
                var description = spec.Definition.Description ?? string.Empty;

                var existsFb = await Repository.DefinitionExists(displayName, env).ConfigureAwait(false);
                EnsureSuccess(existsFb, "DefinitionExists");

                if (!existsFb.Result) {
                    var regDefFb = await Repository.RegisterDefinition(displayName, description, env).ConfigureAwait(false);
                    EnsureSuccess(regDefFb, "RegisterDefinition");
                }

                var defsFb = await Repository.GetAllDefinitions().ConfigureAwait(false);
                EnsureSuccess(defsFb, "GetAllDefinitions");
                var defRows = defsFb.Result ?? new List<Dictionary<string, object>>();

                var defRow = defRows.FirstOrDefault(r => string.Equals(GetString(r, "display_name"), displayName, StringComparison.OrdinalIgnoreCase));
                if (defRow == null) throw new InvalidOperationException($"Definition '{displayName}' not found after registration.");

                var defId = GetLong(defRow, "id");

                var versionString = spec.Definition.Version;
                if (!int.TryParse(versionString, out var versionNumber)) versionNumber = 1;

                var regVerFb = await Repository.RegisterDefinitionVersion(defId, versionNumber, json).ConfigureAwait(false);
                EnsureSuccess(regVerFb, "RegisterDefinitionVersion");

                var latestFb = await Repository.GetLatestDefinitionVersion(defId).ConfigureAwait(false);
                EnsureSuccess(latestFb, "GetLatestDefinitionVersion");
                var verRow = latestFb.Result ?? throw new InvalidOperationException("Latest definition version row is null.");
                var defVersionId = (int)GetLong(verRow, "id");

                var catFb = await Repository.GetAllCategoriesAsync().ConfigureAwait(false);
                EnsureSuccess(catFb, "GetAllCategoriesAsync");
                var catRows = catFb.Result ?? new List<Dictionary<string, object>>();

                var categoryByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in catRows) {
                    var name = GetString(c, "display_name") ?? GetString(c, "name") ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    categoryByName[name] = GetInt(c, "id");
                }

                int ResolveCategoryId(string? category) {
                    var name = string.IsNullOrWhiteSpace(category) ? "business" : category.Trim();
                    if (categoryByName.TryGetValue(name, out var id)) return id;
                    var insertFb = Repository.InsertCategoryAsync(name).GetAwaiter().GetResult();
                    EnsureSuccess(insertFb, "InsertCategoryAsync");
                    var row = insertFb.Result ?? throw new InvalidOperationException("InsertCategoryAsync returned null row.");
                    var newId = GetInt(row, "id");
                    categoryByName[name] = newId;
                    return newId;
                }

                var stateIdByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var eventIdByCode = new Dictionary<int, int>();
                var eventCodeById = new Dictionary<int, int>();
                var eventNameById = new Dictionary<int, string>();

                foreach (var s in spec.States) {
                    var flags = BuildStateFlags(s);
                    var categoryId = ResolveCategoryId(s.Category);
                    var stateFb = await Repository.RegisterState(s.Name, defVersionId, flags, categoryId).ConfigureAwait(false);
                    EnsureSuccess(stateFb, "RegisterState");
                    var row = stateFb.Result ?? throw new InvalidOperationException("RegisterState returned null row.");
                    var stateId = GetInt(row, "id");
                    stateIdByName[s.Name] = stateId;
                }

                foreach (var e in spec.Events) {
                    var display = string.IsNullOrWhiteSpace(e.DisplayName) ? e.Name : e.DisplayName;
                    var evFb = await Repository.RegisterEvent(display,e.Code, defVersionId).ConfigureAwait(false);
                    EnsureSuccess(evFb, "RegisterEvent");
                    var row = evFb.Result ?? throw new InvalidOperationException("RegisterEvent returned null row.");
                    var eventId = GetInt(row, "id");
                    var code = e.Code != 0 ? e.Code : eventId;
                    eventIdByCode[code] = eventId;
                    eventCodeById[eventId] = code;
                    eventNameById[eventId] = display ?? e.Name;
                }

                foreach (var t in spec.Transitions) {
                    if (!stateIdByName.TryGetValue(t.From, out var fromStateId)) throw new InvalidOperationException($"Unknown state in transition 'from': '{t.From}'.");
                    if (!stateIdByName.TryGetValue(t.To, out var toStateId)) throw new InvalidOperationException($"Unknown state in transition 'to': '{t.To}'.");
                    if (!eventIdByCode.TryGetValue(t.Event, out var eventId)) throw new InvalidOperationException($"Unknown event code '{t.Event}' in transition.");

                    var trFb = await Repository.RegisterTransition(fromStateId, toStateId, eventId, defVersionId).ConfigureAwait(false);
                    EnsureSuccess(trFb, "RegisterTransition");
                }

                var result = new DefinitionLoadResult { DefinitionId = defId, DefinitionVersionId = defVersionId, StateCount = spec.States.Count, EventCount = spec.Events.Count, TransitionCount = spec.Transitions.Count };
                fb.SetStatus(true).SetResult(result);
            } catch (Exception ex) {
                fb.SetMessage(ex.Message).SetTrace(ex.StackTrace);
                if (ThrowExceptions || Repository.ThrowExceptions) throw;
            }

            return fb;
        }
    }
}
