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

        static (int definitionVersion, string externalRef) ParseInstanceKey(LifeCycleKey key) {
            var defVersion = key.A is int dv ? dv : int.Parse(key.A?.ToString() ?? "0");
            var externalRef = key.B?.ToString() ?? string.Empty;
            return (defVersion, externalRef);
        }

        private static string BuildMetadata(string? comment, object? context) {
            if (comment == null && context == null) return string.Empty;
            var payload = new Dictionary<string, object?> { ["comment"] = comment, ["context"] = context };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private void EnsureSuccess<T>(IFeedback<T> feedback, string context) {
            if (feedback == null) throw new InvalidOperationException($"{context} returned null feedback.");
            if (feedback.Status) return;
            var message = $"Operation '{context}' failed. Reason : {feedback.Message}";
            if (ThrowExceptions || Repository.ThrowExceptions) {
                throw new InvalidOperationException(message);
            } else {
                Console.WriteLine(message);
            }
        }

        private static void NormalizeDefinitionJson(LifeCycleDefinitionJson spec) {
            spec.Definition.Name = (spec.Definition.Name ?? string.Empty).Trim();
            spec.Definition.Description = (spec.Definition.Description ?? string.Empty).Trim();
            if (spec.Events != null) foreach (var e in spec.Events) { e.Name = (e.Name ?? string.Empty).Trim();  }
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
