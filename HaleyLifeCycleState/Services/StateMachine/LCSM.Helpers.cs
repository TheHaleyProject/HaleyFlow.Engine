using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        private static string NormalizeExternalRef(string externalRef) {
            return externalRef?.Trim() ?? string.Empty;
        }

        private static int GetInt(IDictionary<string, object> row, string key) {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (!row.TryGetValue(key, out var value) || value == null) return 0;
            if (value is int i) return i;
            if (value is long l) return (int)l;
            if (value is short s) return s;
            if (int.TryParse(Convert.ToString(value), out var parsed)) return parsed;
            return 0;
        }

        private static long GetLong(IDictionary<string, object> row, string key) {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (!row.TryGetValue(key, out var value) || value == null) return 0L;
            if (value is long l) return l;
            if (value is int i) return i;
            if (long.TryParse(Convert.ToString(value), out var parsed)) return parsed;
            return 0L;
        }

        private static string? GetString(IDictionary<string, object> row, string key) {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (!row.TryGetValue(key, out var value) || value == null) return null;
            return Convert.ToString(value);
        }

        private static LifeCycleState MapState(IDictionary<string, object> row) {
            return new LifeCycleState { 
                Id = GetInt(row, "id"), 
                DisplayName = GetString(row, "display_name") ?? string.Empty, 
                DefinitionVersion = GetInt(row, "def_version"), 
                Category = GetInt(row, "category"), 
                Flags = (LifeCycleStateFlag)GetInt(row, "flags"), 
                Created = DateTime.UtcNow };
        }

        private static LifeCycleInstance MapInstance(IDictionary<string, object> row) {
            return new LifeCycleInstance { 
                Id = GetLong(row, "id"),
                DefinitionVersion = GetInt(row, "def_version"), 
                CurrentState = GetInt(row, "current_state"), 
                LastEvent = GetInt(row, "last_event"), 
                ExternalRef = GetString(row, "external_ref") ?? string.Empty, 
                Flags = (LifeCycleInstanceFlag)GetInt(row, "flags"), 
                Created = DateTime.UtcNow };
        }

        private static string BuildMetadata(string? comment, object? context) {
            if (comment == null && context == null) return string.Empty;
            var payload = new Dictionary<string, object?> { 
                ["comment"] = comment, 
                ["context"] = context };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private static void NormalizeDefinitionJson(LifeCycleDefinitionJson spec) {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            spec.Definition ??= new DefinitionBlock();
            spec.States ??= new List<StateBlock>();
            spec.Events ??= new List<EventBlock>();
            spec.Transitions ??= new List<TransitionBlock>();

            spec.Definition.Name = spec.Definition.Name?.Trim() ?? string.Empty;
            spec.Definition.Version = spec.Definition.Version?.Trim() ?? "1.0.0";
            spec.Definition.Description = spec.Definition.Description?.Trim();
            spec.Definition.Environment = spec.Definition.Environment?.Trim();

            foreach (var s in spec.States) {
                s.Name = s.Name?.Trim() ?? string.Empty;
                s.Category = s.Category?.Trim() ?? "business";
                s.Timeout = s.Timeout?.Trim();
                s.TimeoutMode = s.TimeoutMode?.Trim();
            }

            foreach (var e in spec.Events) {
                e.Name = e.Name?.Trim() ?? string.Empty;
                e.DisplayName = string.IsNullOrWhiteSpace(e.DisplayName) ? e.Name : e.DisplayName!.Trim();
            }

            foreach (var t in spec.Transitions) {
                t.From = t.From?.Trim() ?? string.Empty;
                t.To = t.To?.Trim() ?? string.Empty;
            }

            if (!spec.States.Any(s => s.IsInitial) && spec.States.Count > 0) spec.States[0].IsInitial = true;
        }

        private static LifeCycleStateFlag BuildStateFlags(StateBlock block) {
            var result = LifeCycleStateFlag.None;
            if (block == null) return result;
            if (block.IsInitial) result |= LifeCycleStateFlag.IsInitial;
            if (block.IsFinal) result |= LifeCycleStateFlag.IsFinal;

            if (block.Flags != null) {
                foreach (var f in block.Flags) {
                    if (string.IsNullOrWhiteSpace(f)) continue;
                    if (Enum.TryParse<LifeCycleStateFlag>(f.Trim(), true, out var parsed)) result |= parsed;
                }
            }

            return result;
        }

        private void EnsureSuccess<T>(IFeedback<T> feedback, string context) {
            if (feedback == null) throw new InvalidOperationException($"{context} returned null feedback.");
            if (feedback.Status) return;
            var message = string.IsNullOrWhiteSpace(feedback.Message) ? $"Operation '{context}' failed." : feedback.Message;
            if (ThrowExceptions || Repository.ThrowExceptions) throw new InvalidOperationException(message);
        }
    }
}
