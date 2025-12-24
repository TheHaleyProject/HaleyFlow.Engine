using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Haley.Services {
    public partial class LifeCycleProcessor {

        public Task<IFeedback<Dictionary<string, object>>> InsertAck(long transitionLogId, int consumer, WorkFlowAckStatus ackStatus = WorkFlowAckStatus.Pending, string? messageId = null) {
            try {
                return Repository.InsertAck(transitionLogId, consumer, (int)ackStatus, messageId);
            } catch (Exception ex) {
                NotifyError(new LifeCycleError {
                    Operation = "Insert Acknowledgement",
                    Data = transitionLogId,
                    Exception = ex,
                    TimeStamp = DateTime.UtcNow,
                    Reference = messageId
                });
                //throw; //breaks here.. May be it might break the pipelines (if inside a Task.WhenAll)
                return Task.FromException<IFeedback<Dictionary<string, object>>>(ex);
            }
        }

        public Task<IFeedback<bool>> MarkAck(string messageId, WorkFlowAckStatus status) {
            try {
                return Repository.MarkAck(messageId, (int)status);
            } catch (Exception ex) {
                NotifyError(new LifeCycleError {
                    Operation = "Mark Acknowledgement",
                    Data = status,
                    Exception = ex,
                    TimeStamp = DateTime.UtcNow,
                    Reference = messageId
                });
                return Task.FromException<IFeedback<bool>>(ex);
            }
        }
    }
}
