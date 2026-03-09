using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK_CONSUMER {

        public const string EXISTS_BY_ID = $@"SELECT 1 FROM ack_consumer WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_ACK_ID_AND_CONSUMER = $@"SELECT 1 FROM ack_consumer WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM ack_consumer WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_ACK_ID_AND_CONSUMER = $@"SELECT * FROM ack_consumer WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID} LIMIT 1;";
        public const string GET_BY_ACK_GUID_AND_CONSUMER = $@"SELECT ac.* FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE a.guid = lower(trim({GUID})) AND ac.consumer = {CONSUMER_ID} LIMIT 1;";
        public const string GET_BY_KEY = $@"SELECT * FROM ack_consumer WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID} LIMIT 1;";

        // inserts must return id; caller sets next_due (can be NULL)
        public const string UPSERT_RETURN_ID = $@"INSERT INTO ack_consumer (ack_id, consumer, status, next_due) VALUES ({ACK_ID}, {CONSUMER_ID}, {ACK_STATUS}, {NEXT_DUE}) ON DUPLICATE KEY UPDATE status = VALUES(status), next_due = VALUES(next_due), modified = CURRENT_TIMESTAMP, id = LAST_INSERT_ID(id); SELECT LAST_INSERT_ID() AS id;";

        // due queue (monitor)
        public const string LIST_DUE_BY_STATUS_PAGED = $@"SELECT ac.*, a.guid AS ack_guid, a.created AS ack_created FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE ac.status = {ACK_STATUS} AND ac.next_due IS NOT NULL AND ac.next_due <= UTC_TIMESTAMP() ORDER BY ac.next_due ASC, ac.id ASC LIMIT {TAKE} OFFSET {SKIP};";
        public const string LIST_DUE_BY_CONSUMER_AND_STATUS_PAGED = $@"SELECT ac.*, a.guid AS ack_guid, a.created AS ack_created FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE ac.consumer = {CONSUMER_ID} AND ac.status = {ACK_STATUS} AND ac.next_due IS NOT NULL AND ac.next_due <= UTC_TIMESTAMP() ORDER BY ac.next_due ASC, ac.id ASC LIMIT {TAKE} OFFSET {SKIP};";

        // mark a trigger attempt + schedule next_due (caller decides next_due; can be NULL)
        public const string MARK_TRIGGER = $@"UPDATE ack_consumer SET trigger_count = trigger_count + 1, last_trigger = CURRENT_TIMESTAMP, next_due = {NEXT_DUE}, modified = CURRENT_TIMESTAMP WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID};";

        // status change + next_due management (Processed/Failed => pass NULL)
        public const string SET_STATUS_AND_DUE = $@"UPDATE ack_consumer SET status = {ACK_STATUS}, next_due = {NEXT_DUE}, modified = CURRENT_TIMESTAMP WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID};";
        public const string SET_STATUS_AND_DUE_BY_GUID = $@"UPDATE ack_consumer ac JOIN ack a ON a.id = ac.ack_id SET ac.status = {ACK_STATUS}, ac.next_due = {NEXT_DUE}, ac.modified = CURRENT_TIMESTAMP WHERE a.guid = lower(trim({GUID})) AND ac.consumer = {CONSUMER_ID};";

        public const string LIST_BY_STATUS_PAGED = $@"SELECT ac.*, a.guid AS ack_guid, a.created AS ack_created FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE ac.status = {ACK_STATUS} ORDER BY ac.modified DESC, ac.id DESC LIMIT {TAKE} OFFSET {SKIP};";
        public const string LIST_BY_CONSUMER_AND_STATUS_PAGED = $@"SELECT ac.*, a.guid AS ack_guid, a.created AS ack_created FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE ac.consumer = {CONSUMER_ID} AND ac.status = {ACK_STATUS} ORDER BY ac.modified DESC, ac.id DESC LIMIT {TAKE} OFFSET {SKIP};";
        public const string PUSH_NEXT_DUE_FOR_DOWN_BY_CONSUMER_AND_STATUS = @$"UPDATE ack_consumer ac JOIN consumer c ON c.id=ac.consumer SET ac.next_due=DATE_ADD(UTC_TIMESTAMP(), INTERVAL {RECHECK_SECONDS} SECOND) WHERE ac.consumer={CONSUMER_ID} AND ac.status={ACK_STATUS} AND ac.next_due IS NOT NULL AND ac.next_due <= UTC_TIMESTAMP() AND TIMESTAMPDIFF(SECOND, c.last_beat, UTC_TIMESTAMP()) > {TTL_SECONDS};";



        // Mark all sibling ack_consumer rows for an ack as Processed (used for ack_mode=Any).
        public const string MARK_ALL_PROCESSED_BY_ACK_ID = $@"UPDATE ack_consumer SET status = 3, next_due = NULL, modified = CURRENT_TIMESTAMP WHERE ack_id = {ACK_ID} AND status NOT IN (3, 4);";

        public const string DELETE = $@"DELETE FROM ack_consumer WHERE id = {ID};";
        public const string DELETE_BY_ACK_ID = $@"DELETE FROM ack_consumer WHERE ack_id = {ACK_ID};";

        public const string LIST_PENDING_DETAIL_PAGED =
            $@"SELECT ac.ack_id, a.guid AS ack_guid, ac.consumer, ac.status, ac.next_due,
              ac.trigger_count, ac.last_trigger, ac.created, ac.modified, a.created AS ack_created,
              i.guid AS instance_guid, i.entity_id, d.name AS def_name, hr.name AS hook_route
       FROM ack_consumer ac
       JOIN ack a ON a.id = ac.ack_id
       LEFT JOIN lc_ack la ON la.ack_id = ac.ack_id
       LEFT JOIN lifecycle lc ON lc.id = la.lc_id
       LEFT JOIN instance li ON li.id = lc.instance_id
       LEFT JOIN hook_ack ha ON ha.ack_id = ac.ack_id
       LEFT JOIN hook_lc hl ON hl.id = ha.hook_id
       LEFT JOIN hook hk ON hk.id = hl.hook_id
       LEFT JOIN hook_route hr ON hr.id = hk.route_id
       LEFT JOIN instance hi ON hi.id = hk.instance_id
       LEFT JOIN instance i ON i.id = COALESCE(li.id, hi.id)
       LEFT JOIN definition d ON d.id = i.def_id
       LEFT JOIN environment e ON e.id = d.env
       WHERE ac.status IN (1, 2)
         AND (e.code IS NULL OR e.code = {CODE})
       ORDER BY ac.next_due ASC, ac.ack_id DESC
       LIMIT {TAKE} OFFSET {SKIP};";
    }
}
