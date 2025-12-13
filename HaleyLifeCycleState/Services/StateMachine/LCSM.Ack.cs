using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleStateMachine {

        public Task<IFeedback<Dictionary<string, object>>> Ack_Insert(long transitionLogId, int consumer, int ackStatus = 1, string? messageId = null) =>
            Repository.InsertAck(transitionLogId, consumer, ackStatus, messageId);

        public Task<IFeedback<bool>> Ack_Mark(LifeCycleKey key, LifeCycleAckStatus status) =>
            Repository.MarkAck(key, status);
    }
}
