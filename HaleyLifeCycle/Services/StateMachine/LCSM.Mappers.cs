using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Text.Json;
using Haley.Utils;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        //Haley Library has a Mapper class but we are implementing our own here for BETTER PERFORMANCE because the Haley mappers are based on Reflection.
        private static LifeCycleState MapState(IDictionary<string, object> row) => new() {
            Id = row.GetInt("id"),
            DisplayName = row.GetString("display_name") ?? string.Empty,
            DefinitionVersion = row.GetInt("def_version"),
            Category = row.GetInt("category"),
            Flags = (LifeCycleStateFlag)row.GetInt("flags"),
            TimeoutMinutes = row.GetNullableInt("timeout_minutes"),
            TimeoutMode = row.GetInt("timeout_mode"),
            TimeoutEvent = row.GetInt("timeout_event"),
            Created = row.GetDateTime("created") ?? DateTime.UtcNow,
            Modified = row.GetDateTime("modified") ?? DateTime.UtcNow
        };

        private static LifeCycleInstance MapInstance(IDictionary<string, object> row) => new() {
            Id = row.GetLong("id"),
            DefinitionVersion = row.GetInt("def_version"),
            CurrentState = row.GetInt("current_state"),
            LastEvent = row.GetInt("last_event"),
            ExternalRef = row.GetString("external_ref") ?? string.Empty,
            Flags = (LifeCycleInstanceFlag)row.GetInt("flags"),
            Created = row.GetDateTime("created") ?? DateTime.UtcNow,
            Modified = row.GetDateTime("modified") ?? DateTime.UtcNow,
            Guid = row.GetGuid("guid") ?? Guid.Empty
        };

        private static LifeCycleEvent MapEvent(IDictionary<string, object> row) => new() {
            Id = row.GetInt("id"),
            DefinitionVersion = row.GetInt("def_version"),
            DisplayName = row.GetString("display_name") ?? string.Empty,
            Code = row.GetInt("code"),
        };

        private static LifeCycleTransition MapTransition(IDictionary<string, object> row) => new() {
            Id = row.GetInt("id"),
            DefinitionVersion = row.GetInt("def_version"),
            FromState = row.GetInt("from_state"),
            ToState = row.GetInt("to_state"),
            Event = row.GetInt("event"),
            Created = row.GetDateTime("created") ?? DateTime.UtcNow,
        };

        private static LifeCycleTransitionLog MapTransitionLog(IDictionary<string, object> row) => new() {
            Id = row.GetLong("id"),
            InstanceId = row.GetLong("instance_id"),
            FromState = row.GetInt("from_state"),
            ToState = row.GetInt("to_state"),
            Event = row.GetInt("event"),
            Created = row.GetDateTime("created") ?? DateTime.UtcNow,
            Actor = row.GetString("actor"),
            Metadata = row.GetString("metadata")
        };
    }
}
