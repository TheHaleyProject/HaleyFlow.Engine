using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStoreMaria {

        public async Task<IFeedback<Dictionary<string, object>>> InsertAck(long transitionLogId, int consumer, int ackStatus = 1, string? messageId = null) {
            if (!string.IsNullOrWhiteSpace(messageId)) {
                var exMsg = await _agw.ReadSingleAsync(_key, QRY_ACK_LOG.GET_BY_MESSAGE, (MESSAGE_ID, messageId!)).ConfigureAwait(false);
                if (exMsg.Status && exMsg.Result != null && exMsg.Result.Count > 0) return exMsg;
                return await _agw.ReadSingleAsync(_key, QRY_ACK_LOG.INSERT_WITH_MESSAGE, (TRANSITION_LOG, transitionLogId), (CONSUMER, consumer), (ACK_STATUS, ackStatus), (MESSAGE_ID, messageId!)).ConfigureAwait(false);
            }

            var ex = await _agw.ReadSingleAsync(_key,QRY_ACK_LOG.GET_BY_TL_AND_CONSUMER, (TRANSITION_LOG, transitionLogId), (CONSUMER, consumer)).ConfigureAwait(false);
            if (ex.Status && ex.Result != null && ex.Result.Count > 0) return ex;
            return await _agw.ReadSingleAsync(_key, QRY_ACK_LOG.INSERT, (TRANSITION_LOG, transitionLogId), (CONSUMER, consumer), (ACK_STATUS, ackStatus)).ConfigureAwait(false);
        }

        public Task<IFeedback<bool>> MarkAck(string messageId, int ackStatus) {
            return _agw.NonQueryAsync(_key, QRY_ACK_LOG.MARK_BY_MESSAGE, (MESSAGE_ID, messageId), (ACK_STATUS, ackStatus));
        }

        public Task<IFeedback<List<Dictionary<string, object>>>> GetAck(WorkFlowAckFetchMode mode, int maxRetry, int retryAfterMinutes, int skip = 0, int limit = 200) =>
            mode == WorkFlowAckFetchMode.DueForRetry
                ? _agw.ReadAsync(_key, QRY_ACK_LOG.GET_DUE_FOR_RETRY, (MAX_RETRY, maxRetry), (RETRY_AFTER_MIN, retryAfterMinutes), (SKIP, skip), (LIMIT, limit))
                : _agw.ReadAsync(_key, QRY_ACK_LOG.GET_FAILED, (SKIP, skip), (LIMIT, limit));

        public Task<IFeedback<bool>> RetryAck(long ackId) =>
            _agw.NonQueryAsync(_key, QRY_ACK_LOG.BUMP_RETRY, (ID, ackId));

    }
}
