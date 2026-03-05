using Haley.Models;
using Haley.Abstractions;
using Haley.Enums;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Haley.Models {
    internal sealed class LifeCycleHookEvent : LifeCycleEvent, ILifeCycleHookEvent {
        public LifeCycleEventKind Kind { get { return LifeCycleEventKind.Hook; } }
        public bool OnEntry { get; set; }
        public string Route { get; set; }
        public DateTimeOffset? NotBefore { get; set; }
        public DateTimeOffset? Deadline { get; set; }
        public bool IsBlocking { get; set; } = true;
        public string? GroupName { get; set; }
        public LifeCycleHookEvent() { }
        public LifeCycleHookEvent(LifeCycleEvent evt) : base(evt) { }
    }
}
