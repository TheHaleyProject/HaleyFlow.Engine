using Haley.Abstractions;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {

        public Task<IFeedback<Dictionary<string, object>>> Ack_Insert(long transitionLogId, int consumer, int ackStatus = 1) =>
            _agw.ReadSingleAsync(_key, QRY_ACK_LOG.INSERT, (TRANSITION_LOG, transitionLogId), (CONSUMER, consumer), (ACK_STATUS, ackStatus));

        public Task<IFeedback<Dictionary<string, object>>> Ack_InsertWithMessage(long transitionLogId, int consumer, string messageId, int ackStatus = 1) =>
            _agw.ReadSingleAsync(_key, QRY_ACK_LOG.INSERT_WITH_MESSAGE, (TRANSITION_LOG, transitionLogId), (CONSUMER, consumer), (ACK_STATUS, ackStatus), (MESSAGE_ID, messageId));

        public Task<IFeedback<bool>> Ack_MarkDeliveredByMessage(string messageId) =>
            _agw.NonQueryAsync(_key, QRY_ACK_LOG.MARK_DELIVERED_BY_MESSAGE, (MESSAGE_ID, messageId));

        public Task<IFeedback<bool>> Ack_MarkProcessedByMessage(string messageId) =>
            _agw.NonQueryAsync(_key, QRY_ACK_LOG.MARK_PROCESSED_BY_MESSAGE, (MESSAGE_ID, messageId));

        public Task<IFeedback<bool>> Ack_MarkFailedByMessage(string messageId) =>
            _agw.NonQueryAsync(_key, QRY_ACK_LOG.MARK_FAILED_BY_MESSAGE, (MESSAGE_ID, messageId));

        public Task<IFeedback<bool>> Ack_MarkDelivered(long transitionLogId, int consumer) =>
            _agw.NonQueryAsync(_key, QRY_ACK_LOG.MARK_DELIVERED, (TRANSITION_LOG, transitionLogId), (CONSUMER, consumer));

        public Task<IFeedback<bool>> Ack_MarkProcessed(long transitionLogId, int consumer) =>
            _agw.NonQueryAsync(_key, QRY_ACK_LOG.MARK_PROCESSED, (TRANSITION_LOG, transitionLogId), (CONSUMER, consumer));

        public Task<IFeedback<bool>> Ack_MarkFailed(long transitionLogId, int consumer) =>
            _agw.NonQueryAsync(_key, QRY_ACK_LOG.MARK_FAILED, (TRANSITION_LOG, transitionLogId), (CONSUMER, consumer));

        public Task<IFeedback<List<Dictionary<string, object>>>> Ack_GetDueForRetry(int maxRetry, int retryAfterMinutes) =>
            _agw.ReadAsync(_key, QRY_ACK_LOG.GET_DUE_FOR_RETRY, (MAX_RETRY, maxRetry), (RETRY_AFTER_MIN, retryAfterMinutes));

        public Task<IFeedback<bool>> Ack_BumpRetry(long ackId) =>
            _agw.NonQueryAsync(_key, QRY_ACK_LOG.BUMP_RETRY, (ID, ackId));
    }
}
