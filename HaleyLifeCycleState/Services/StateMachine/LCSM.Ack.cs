using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        public Task<IFeedback<Dictionary<string, object>>> InsertAck(long transitionLogId, int consumer, LifeCycleAckStatus ackStatus = LifeCycleAckStatus.Pending, string? messageId = null) =>
            Repository.InsertAck(transitionLogId, consumer, (int) ackStatus, messageId);

        public Task<IFeedback<bool>> MarkAck(string messageId, LifeCycleAckStatus status) =>
            Repository.MarkAck(messageId, (int)status);
    }
}
