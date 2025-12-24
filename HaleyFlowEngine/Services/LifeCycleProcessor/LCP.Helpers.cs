using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Haley.Utils;

namespace Haley.Services {
    public partial class LifeCycleProcessor {

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        private static string BuildMetadata(string? comment, object? context) {
            if (comment == null && context == null) return string.Empty;
            var payload = new Dictionary<string, object?> { ["comment"] = comment, ["context"] = context };
            return JsonSerializer.Serialize(payload, JsonOptions);
        }

        private bool EnsureSuccess<T>(IFeedback<T> feedback, string context, bool throwError = true) {
            string errorMessage = string.Empty;
            do {
                if (feedback == null) {
                    errorMessage =$"{context} returned null feedback.";
                    break; 
                }
                if (feedback.Status) return true; //On successfull status.
                errorMessage = $"Operation '{context}' failed. Reason : {feedback.Message}";
            } while (false);

            if (throwError) throw new InvalidOperationException(errorMessage);
            Console.WriteLine(errorMessage);
            return false;
        }

        private static void NormalizeDefinitionJson(LifeCycleDefinitionJson spec) {
            spec.Definition.Name = (spec.Definition.Name ?? string.Empty).Trim();
            spec.Definition.Description = (spec.Definition.Description ?? string.Empty).Trim();
            if (spec.Events != null) foreach (var e in spec.Events) { e.Name = (e.Name ?? string.Empty).Trim();  }
            if (spec.States != null) foreach (var s in spec.States) { s.Name = (s.Name ?? string.Empty).Trim(); s.Category = (s.Category ?? "business").Trim(); s.Timeout = (s.Timeout ?? string.Empty).Trim(); s.TimeoutMode = (s.TimeoutMode ?? string.Empty).Trim(); }
            if (spec.Transitions != null) foreach (var t in spec.Transitions) { t.From = (t.From ?? string.Empty).Trim(); t.To = (t.To ?? string.Empty).Trim(); }
        }

        private static WorkFlowStateFlag BuildStateFlags(StateBlock s) {
            var f = WorkFlowStateFlag.None;
            if (s.IsInitial) f |= WorkFlowStateFlag.IsInitial;
            if (s.IsFinal) f |= WorkFlowStateFlag.IsFinal;
            if (!string.IsNullOrWhiteSpace(s.Category)) {
                var c = s.Category.Trim();
                if (c.Equals("system", StringComparison.OrdinalIgnoreCase)) f |= WorkFlowStateFlag.IsSystem;
                if (c.Equals("error", StringComparison.OrdinalIgnoreCase)) f |= WorkFlowStateFlag.IsError;
            }
            return f;
        }

    }
}
