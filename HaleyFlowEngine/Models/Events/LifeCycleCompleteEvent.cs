using Haley.Abstractions;
using Haley.Enums;

namespace Haley.Models {
    internal sealed class LifeCycleCompleteEvent : LifeCycleEvent, ILifeCycleCompleteEvent {
        public override LifeCycleEventKind Kind => LifeCycleEventKind.Complete;
        public long LifeCycleId { get; set; }
        public bool HooksSucceeded { get; set; }
        public int NextEvent { get; set; }
        public LifeCycleCompleteEvent() { }
        public LifeCycleCompleteEvent(LifeCycleEvent src) : base(src) { }
    }
}
