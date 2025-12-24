using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleProcessor {

        void NotifyTransition(TransitionOccurred occurred) {
            var handler = TransitionRaised;
            if (handler == null) return;

            foreach (var d in handler.GetInvocationList()) {
                _ = Task.Run(async () => {
                    try {
                        if (d is Func<TransitionOccurred, Task> asyncHandler)
                            await asyncHandler(occurred).ConfigureAwait(false);
                        else
                            d.DynamicInvoke(occurred);
                    } catch (Exception ex) {
                        NotifyError(new LifeCycleError {
                            Operation = "TransitionRaised",
                            Data = occurred,
                            Exception = ex,
                            TimeStamp = DateTime.UtcNow,
                            Reference = occurred.ExternalRef
                        });
                    }
                });
            }
        }

        void NotifyError(LifeCycleError err) {
            var handler = ErrorRaised;
            if (handler == null) return;

            foreach (var d in handler.GetInvocationList()) {
                _ = Task.Run(async () => {
                    try {
                        if (d is Func<LifeCycleError, Task> asyncHandler)
                            await asyncHandler(err).ConfigureAwait(false);
                        else
                            d.DynamicInvoke(err);
                    } catch {
                        // Swallow to avoid infinite loops
                    }
                });
            }
        }

        internal void NotifyTimeout(TimeoutNotification timeoutObj) {
            var handler = TimeoutRaised;
            if (handler == null) return;

            foreach (var d in handler.GetInvocationList()) {
                _ = Task.Run(async () => {
                    try {
                        if (d is Func<TimeoutNotification, Task> asyncHandler)
                            await asyncHandler(timeoutObj).ConfigureAwait(false);
                        else
                            d.DynamicInvoke(timeoutObj);
                    } catch (Exception ex) {
                        NotifyError(new LifeCycleError {
                            Operation = "TimeoutRaised",
                            Data = timeoutObj,
                            Exception = ex,
                            TimeStamp = DateTime.UtcNow,
                            Reference = timeoutObj.ExternalRef
                        });
                    }
                });
            }
        }

        internal void SendNotice(LifeCycleNotice notice) {
            var handler = NoticeRaised;
            if (handler == null) return;

            foreach (var d in handler.GetInvocationList()) {
                _ = Task.Run(async () =>
                {
                    try {
                        if (d is Func<LifeCycleNotice, Task> asyncHandler)
                            await asyncHandler(notice).ConfigureAwait(false);
                        else
                            d.DynamicInvoke(notice);
                    } catch {
                        // swallow: notice pipeline must never break the state machine
                    }
                });
            }
        }
    }
}
