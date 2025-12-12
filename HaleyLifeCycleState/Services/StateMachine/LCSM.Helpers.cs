using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        static string NormalizeExternalRef(string externalRef) =>
            string.IsNullOrWhiteSpace(externalRef) ? string.Empty : externalRef.Trim().ToLowerInvariant();

        static int ToInt(object? v) => v == null ? 0 : Convert.ToInt32(v);
        static long ToLong(object? v) => v == null ? 0L : Convert.ToInt64(v);
        static string? ToStr(object? v) => v?.ToString();

        static int ToInt(Dictionary<string, object> row, string key) =>
            row.TryGetValue(key, out var v) ? Convert.ToInt32(v) : 0;

        static long ToLong(Dictionary<string, object> row, string key) =>
            row.TryGetValue(key, out var v) ? Convert.ToInt64(v) : 0L;

        static string? GetStr(Dictionary<string, object> row, string key) =>
            row.TryGetValue(key, out var v) ? v?.ToString() : null;

        static LifeCycleState MapState(Dictionary<string, object> row) => new LifeCycleState {
            Id = ToInt(row["id"]),
            DisplayName = ToStr(row["display_name"]),
            Flags = (LifeCycleStateFlag)ToInt(row["flags"]),
            DefinitionVersion = ToInt(row["def_version"]),
            Category = ToInt(row["category"])
        };

        static LifeCycleInstance MapInstance(Dictionary<string, object> row) => new LifeCycleInstance {
            Id = ToLong(row["id"]),
            Guid = Guid.TryParse(ToStr(row["guid"]), out var g) ? g : Guid.Empty,
            CurrentState = ToInt(row["current_state"]),
            LastEvent = ToInt(row["last_event"]),
            ExternalRef = ToStr(row["external_ref"]),
            DefinitionVersion = ToInt(row["def_version"]),
            Flags = (LifeCycleInstanceFlag)ToInt(row["flags"])
        };

        async Task ThrowIfFailed<T>(IFeedback<T> feedback, string context) {
            if (feedback == null || !feedback.Status) {
                if (_repo.ThrowExceptions)
                    throw new InvalidOperationException($"{context} failed: {feedback?.Message}");
            }
        }

        async Task RaiseAsync(Func<TransitionEventArgs, Task>? handler, LifeCycleTransitionLog? log = null, Exception? ex = null) {
            if (handler != null) await handler(new TransitionEventArgs(log, ex));
        }

        static readonly JsonSerializerOptions _jsonOptions = new() {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        static string? BuildMetadata(string? comment, object? context) {
            if (context == null) return string.IsNullOrWhiteSpace(comment) ? null : comment;

            // If caller passed a raw string context and no comment, keep it simple.
            if (string.IsNullOrWhiteSpace(comment) && context is string s) return s;

            try {
                return JsonSerializer.Serialize(new { comment, context }, _jsonOptions);
            } catch {
                // Fall back: avoid throwing from logging.
                return string.IsNullOrWhiteSpace(comment) ? context.ToString() : comment;
            }
        }

        static void NormalizeSpec(DefinitionJson s) {
            if (s.Environment == null) s.Environment = "Development";
            if (s.Definition == null) throw new InvalidOperationException("Missing 'definition' block.");
            if (string.IsNullOrWhiteSpace(s.Definition.Name)) throw new InvalidOperationException("definition.name is required.");
            if (s.States == null || s.States.Count == 0) throw new InvalidOperationException("At least one state required.");
            if (!s.States.Any(x => x.IsInitial)) s.States[0].IsInitial = true;
            if (s.Transitions == null) s.Transitions = new();
        }

        static int ResolveEnvironment(string? env) {
            if (string.IsNullOrWhiteSpace(env)) return 0;
            if (int.TryParse(env, out var n)) return n;
            return env.Trim().ToLowerInvariant() switch {
                "dev" or "development" => 0,
                "test" or "qa" or "staging" => 1,
                "prod" or "production" => 2,
                _ => 0
            };
        }

        static int ParseVersionInt(string? versionLabel, int? versionCode) {
            if (versionCode.HasValue && versionCode.Value > 0) return versionCode.Value;
            if (string.IsNullOrWhiteSpace(versionLabel)) return 10000;
            var p = versionLabel.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            int x = p.Length > 0 && int.TryParse(p[0], out var a) ? a : 1;
            int y = p.Length > 1 && int.TryParse(p[1], out var b) ? b : 0;
            int z = p.Length > 2 && int.TryParse(p[2], out var c) ? c : 0;
            return x * 10000 + y * 100 + z;
        }

        static LifeCycleStateFlag BuildStateFlags(StateSpec s) {
            var f = LifeCycleStateFlag.None;
            if (s.IsInitial) f |= LifeCycleStateFlag.IsInitial;
            if (s.IsFinal) f |= LifeCycleStateFlag.IsFinal;
            if (!string.IsNullOrWhiteSpace(s.Category) && s.Category.Equals("error", StringComparison.OrdinalIgnoreCase))
                f |= LifeCycleStateFlag.IsError;
            if (s.Flags != null) {
                foreach (var token in s.Flags) {
                    if (Enum.TryParse<LifeCycleStateFlag>(token, true, out var add)) f |= add;
                }
            }
            return f;
        }

        static LifeCycleTransitionFlag BuildTransitionFlags(TransitionSpec t) {
            var f = LifeCycleTransitionFlag.None;
            if (t.Flags != null) {
                foreach (var token in t.Flags) {
                    if (Enum.TryParse<LifeCycleTransitionFlag>(token, true, out var add)) f |= add;
                }
            }
            return f;
        }
    }
}
