using Haley.Models;
using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class LifeCycleTransitionEvent : LifeCycleEvent, ILifeCycleTransitionEvent {
        public override LifeCycleEventKind Kind => LifeCycleEventKind.Transition;
        public long LifeCycleId { get; set; }
        public long FromStateId { get; set; }
        public long ToStateId { get; set; }
        public int EventCode { get; set; }
        public string EventName { get; set; }
        public TransitionDispatchMode DispatchMode { get; set; }
        public IReadOnlyDictionary<string, object> PrevStateMeta { get; set; }
        public LifeCycleTransitionEvent() : base() { }
        public LifeCycleTransitionEvent(LifeCycleEvent evt) : base(evt) { }
    }
}
