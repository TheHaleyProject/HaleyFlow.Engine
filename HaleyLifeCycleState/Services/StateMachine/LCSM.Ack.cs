using System.Collections.Generic;
using System.Threading.Tasks;
using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;

namespace Haley.Services {
    public partial class LifeCycleStateMachine : ILifeCycleStateMachine {

        public Task<IFeedback<Dictionary<string, object>>> Ack_Insert(long transitionLogId, int consumer, int ackStatus = 1) {
            return Repository.Ack_Insert(transitionLogId, consumer, ackStatus);
        }

        public Task<IFeedback<Dictionary<string, object>>> Ack_InsertWithMessage(long transitionLogId, int consumer, string messageId, int ackStatus = 1) {
            return Repository.Ack_InsertWithMessage(transitionLogId, consumer, messageId, ackStatus);
        }

        public Task<IFeedback<bool>> Ack_MarkByMessageAsync(string messageId, LifeCycleAckStatus status) {
            switch (status) {
                case LifeCycleAckStatus.Delivered:
                return Repository.Ack_MarkDeliveredByMessage(messageId);
                case LifeCycleAckStatus.Processed:
                return Repository.Ack_MarkProcessedByMessage(messageId);
                case LifeCycleAckStatus.Failed:
                return Repository.Ack_MarkFailedByMessage(messageId);
                default:
                return Repository.Ack_MarkFailedByMessage(messageId);
            }
        }

        public Task<IFeedback<bool>> Ack_MarkByTransitionAsync(long transitionLogId, int consumer, LifeCycleAckStatus status) {
            switch (status) {
                case LifeCycleAckStatus.Delivered:
                return Repository.Ack_MarkDelivered(transitionLogId, consumer);
                case LifeCycleAckStatus.Processed:
                return Repository.Ack_MarkProcessed(transitionLogId, consumer);
                case LifeCycleAckStatus.Failed:
                return Repository.Ack_MarkFailed(transitionLogId, consumer);
                default:
                return Repository.Ack_MarkFailed(transitionLogId, consumer);
            }
        }
    }
}
