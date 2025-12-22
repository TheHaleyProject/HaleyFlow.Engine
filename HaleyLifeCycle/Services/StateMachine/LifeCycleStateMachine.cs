using Haley.Abstractions;
using Haley.Models;
using System;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine : IStateMachineRuntime {
        public IStateMachineRepo Repository { get; }
        public event Func<TransitionOccurred, Task>? TransitionRaised;
        public event Func<StateMachineError, Task>? ErrorRaised;
        public event Func<TimeoutNotification, Task>? TimeoutRaised;
        public event Func<StateMachineNotice, Task>? NoticeRaised;

        public LifeCycleStateMachine(IStateMachineRepo repository) {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }
    }
}
