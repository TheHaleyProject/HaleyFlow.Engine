using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Haley.Utils;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static LifeCycleState MapState(IDictionary<string, object> row) => new() {
            Id = row.GetInt("id"),
            DisplayName = row.GetString("display_name") ?? string.Empty,
            DefinitionVersion = row.GetInt("def_version"),
            Category = row.GetInt("category"),
            Flags = (LifeCycleStateFlag)row.GetInt("flags"),
            Created = DateTime.UtcNow
        };

        private static LifeCycleInstance MapInstance(IDictionary<string, object> row) => new() {
            Id = row.GetLong("id"),
            DefinitionVersion = row.GetInt("def_version"),
            CurrentState = row.GetInt("current_state"),
            LastEvent = row.GetInt("last_event"),
            ExternalRef = row.GetString("external_ref") ?? string.Empty,
            Flags = (LifeCycleInstanceFlag)row.GetInt("flags"),
            Created = DateTime.UtcNow
        };

        private static string BuildMetadata(string? comment, object? context) {
            if (comment == null && context == null) return string.Empty;
            var payload = new Dictionary<string, object?> { ["comment"] = comment, ["context"] = context };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private void EnsureSuccess<T>(IFeedback<T> feedback, string context) {
            if (feedback == null) throw new InvalidOperationException($"{context} returned null feedback.");
            if (feedback.Status) return;
            var message = string.IsNullOrWhiteSpace(feedback.Message) ? $"Operation '{context}' failed." : feedback.Message;
            if (ThrowExceptions || Repository.ThrowExceptions) throw new InvalidOperationException(message);
        }

        private static string NormalizeRef(string s) => (s ?? string.Empty).Trim().ToLowerInvariant();

        private static string InstanceKeyToExternalRef(LifeCycleKey key) {
            return key.Type switch {
                LifeCycleKeyType.Guid => ((Guid)key.A).ToString("D"),
                LifeCycleKeyType.Name => NormalizeRef((string)key.A),
                _ => throw new NotSupportedException("Instance external_ref requires LifeCycleKeyType.Name or Guid.")
            };
        }

        private static LifeCycleKey ToRepoInstanceKey(int defVersion, LifeCycleKey instanceKey) {
            return instanceKey.Type switch {
                LifeCycleKeyType.Id => instanceKey,
                LifeCycleKeyType.Guid => instanceKey,
                LifeCycleKeyType.Name => new LifeCycleKey(LifeCycleKeyType.Composite, defVersion, InstanceKeyToExternalRef(instanceKey)),
                _ => throw new NotSupportedException($"Unsupported instance key type: {instanceKey.Type}")
            };
        }

        private static void NormalizeDefinitionJson(LifeCycleDefinitionJson spec) {
            spec.Definition.Name = (spec.Definition.Name ?? string.Empty).Trim();
            spec.Definition.Version = (spec.Definition.Version ?? "1").Trim();
            spec.Definition.Description = (spec.Definition.Description ?? string.Empty).Trim();
            if (spec.Events != null) foreach (var e in spec.Events) { e.Name = (e.Name ?? string.Empty).Trim(); e.DisplayName = (e.DisplayName ?? e.Name).Trim(); }
            if (spec.States != null) foreach (var s in spec.States) { s.Name = (s.Name ?? string.Empty).Trim(); s.Category = (s.Category ?? "business").Trim(); s.Timeout = (s.Timeout ?? string.Empty).Trim(); s.TimeoutMode = (s.TimeoutMode ?? string.Empty).Trim(); }
            if (spec.Transitions != null) foreach (var t in spec.Transitions) { t.From = (t.From ?? string.Empty).Trim(); t.To = (t.To ?? string.Empty).Trim(); }
        }

        private static LifeCycleStateFlag BuildStateFlags(StateBlock s) {
            var f = LifeCycleStateFlag.None;
            if (s.IsInitial) f |= LifeCycleStateFlag.IsInitial;
            if (s.IsFinal) f |= LifeCycleStateFlag.IsFinal;
            if (!string.IsNullOrWhiteSpace(s.Category)) {
                var c = s.Category.Trim();
                if (c.Equals("system", StringComparison.OrdinalIgnoreCase)) f |= LifeCycleStateFlag.IsSystem;
                if (c.Equals("error", StringComparison.OrdinalIgnoreCase)) f |= LifeCycleStateFlag.IsError;
            }
            return f;
        }

    }
}
