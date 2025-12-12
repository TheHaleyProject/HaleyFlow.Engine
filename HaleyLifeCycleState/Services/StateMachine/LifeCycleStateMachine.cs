using Haley.Abstractions;
using Haley.Models;
using System;

namespace Haley.Services {
    public partial class LifeCycleStateMachine : ILifeCycleStateMachine {
        public ILifeCycleStateRepository Repository { get; }
        public bool ThrowExceptions { get; }
        public event Func<TransitionOccurred, Task>? TransitionRaised;
        public LifeCycleStateMachine(ILifeCycleStateRepository repository, bool throwExceptions = false) {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            ThrowExceptions = throwExceptions;
        }
    }
}
