using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK_LOG {
        public const string INSERT = $@"INSERT IGNORE INTO ack_log (transition_log, consumer, ack_status) VALUES ({TRANSITION_LOG}, {CONSUMER}, {ACK_STATUS}); SELECT * FROM ack_log WHERE consumer = {CONSUMER} AND transition_log = {TRANSITION_LOG} LIMIT 1;";
        public const string INSERT_WITH_MESSAGE = $@"INSERT IGNORE INTO ack_log (transition_log, consumer, ack_status, message_id) VALUES ({TRANSITION_LOG}, {CONSUMER}, {ACK_STATUS}, {MESSAGE_ID}); SELECT * FROM ack_log WHERE consumer = {CONSUMER} AND transition_log = {TRANSITION_LOG} LIMIT 1;";
        public const string MARK_DELIVERED_BY_MESSAGE = $@"UPDATE ack_log SET ack_status = 2, modified = utc_timestamp() WHERE message_id = {MESSAGE_ID};";
        public const string MARK_PROCESSED_BY_MESSAGE = $@"UPDATE ack_log SET ack_status = 3, modified = utc_timestamp() WHERE message_id = {MESSAGE_ID};";
        public const string MARK_FAILED_BY_MESSAGE = $@"UPDATE ack_log SET ack_status = 4, modified = utc_timestamp() WHERE message_id = {MESSAGE_ID};";
        public const string MARK_DELIVERED = $@"UPDATE ack_log SET ack_status = 2, modified = utc_timestamp() WHERE transition_log = {TRANSITION_LOG} AND consumer = {CONSUMER};";
        public const string MARK_PROCESSED = $@"UPDATE ack_log SET ack_status = 3, modified = utc_timestamp() WHERE transition_log = {TRANSITION_LOG} AND consumer = {CONSUMER};";
        public const string MARK_FAILED = $@"UPDATE ack_log SET ack_status = 4, modified = utc_timestamp() WHERE transition_log = {TRANSITION_LOG} AND consumer = {CONSUMER};";
        public const string GET_DUE_FOR_RETRY = $@"SELECT id, transition_log, consumer, message_id, ack_status, retry_count, last_retry FROM ack_log WHERE ack_status NOT IN (3, 4) AND retry_count < {MAX_RETRY} AND TIMESTAMPDIFF(MINUTE, last_retry, utc_timestamp()) >= {RETRY_AFTER_MIN} ORDER BY last_retry ASC, id ASC;";
        public const string BUMP_RETRY = $@"UPDATE ack_log SET retry_count = retry_count + 1, last_retry = utc_timestamp(), modified = utc_timestamp() WHERE id = {ID};";
    }
}
