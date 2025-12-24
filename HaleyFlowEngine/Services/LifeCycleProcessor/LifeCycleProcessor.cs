using Haley.Abstractions;
using Haley.Models;
using System;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleProcessor : IStateMachine {
        public IStateRepo Repository { get; }
        public event Func<TransitionOccurred, Task>? TransitionRaised;
        public event Func<LifeCycleError, Task>? ErrorRaised;
        public event Func<TimeoutNotification, Task>? TimeoutRaised;
        public event Func<LifeCycleNotice, Task>? NoticeRaised;

        public LifeCycleProcessor(IStateRepo repository) {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
        }
    }
}
