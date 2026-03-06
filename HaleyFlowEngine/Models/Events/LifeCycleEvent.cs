using Haley.Models;
using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Models {
    internal class LifeCycleEvent : ILifeCycleEvent {
        public virtual LifeCycleEventKind Kind { get; }
        public long ConsumerId { get; set; }
        public string InstanceGuid { get; set; }
        public long DefinitionId { get; set; }
        public long DefinitionVersionId { get; set; }
        public string EntityId { get; set; }
        public string AckGuid { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string? OnSuccessEvent { get; set; }
        public string? OnFailureEvent { get; set; }
        public bool AckRequired { get; set; }
        public string? Metadata { get; set; }
        public IReadOnlyList<LifeCycleParamItem>? Params { get; set; }
        public LifeCycleEvent() { }
        public LifeCycleEvent(LifeCycleEvent source) {
            Kind = source.Kind;
            ConsumerId = source.ConsumerId;
            InstanceGuid = source.InstanceGuid;
            DefinitionId = source.DefinitionId;
            DefinitionVersionId = source.DefinitionVersionId;
            EntityId = source.EntityId;
            AckGuid = source.AckGuid;
            Metadata = source.Metadata;
            Params = source.Params;
            OnSuccessEvent = source.OnSuccessEvent;
            OnFailureEvent = source.OnFailureEvent;
            OccurredAt = source.OccurredAt;
            AckRequired = source.AckRequired;
        }
    }
}
