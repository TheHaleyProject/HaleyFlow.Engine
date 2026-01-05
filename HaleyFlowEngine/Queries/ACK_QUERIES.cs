using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM ack WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM ack WHERE guid = lower(trim({GUID})) LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM ack WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT * FROM ack WHERE guid = lower(trim({GUID})) LIMIT 1;";

        public const string GET_ID_BY_GUID = $@"SELECT id FROM ack WHERE guid = lower(trim({GUID})) LIMIT 1;";
        public const string GET_GUID_BY_ID = $@"SELECT guid FROM ack WHERE id = {ID} LIMIT 1;";

        // creates a new ack; returns both id + guid (engine needs id for link tables, guid for app)
        public const string INSERT = $@"INSERT INTO ack () VALUES (); SELECT id AS id, guid AS guid FROM ack WHERE id = LAST_INSERT_ID() LIMIT 1;";

        public const string INSERT_WITH_GUID = $@"INSERT INTO ack (guid) VALUES (lower(trim({GUID}))); SELECT id AS id, guid AS guid FROM ack WHERE id = LAST_INSERT_ID() LIMIT 1;";

        public const string DELETE = $@"DELETE FROM ack WHERE id = {ID};";
    }
    internal class QRY_ACK_CONSUMER {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM ack_consumer WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_ACK_ID_AND_CONSUMER = $@"SELECT 1 FROM ack_consumer WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID} LIMIT 1;";

        public const string GET_BY_ID = $@"SELECT * FROM ack_consumer WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_ACK_ID_AND_CONSUMER = $@"SELECT * FROM ack_consumer WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID} LIMIT 1;";
        public const string GET_BY_ACK_GUID_AND_CONSUMER = $@"SELECT ac.* FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE a.guid = lower(trim({GUID})) AND ac.consumer = {CONSUMER_ID} LIMIT 1;";
        public const string GET_BY_KEY = $@"SELECT * FROM ack_consumer WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID} LIMIT 1;";

        // inserts must return id
        public const string UPSERT_RETURN_ID = $@"INSERT INTO ack_consumer (ack_id, consumer, status) VALUES ({ACK_ID}, {CONSUMER_ID}, {ACK_STATUS}) ON DUPLICATE KEY UPDATE status = VALUES(status), modified = CURRENT_TIMESTAMP, id = LAST_INSERT_ID(id); SELECT LAST_INSERT_ID() AS id;";

        // paging / monitoring
        public const string LIST_BY_STATUS_PAGED = $@"SELECT ac.*, a.guid AS ack_guid, a.created AS ack_created FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE ac.status = {ACK_STATUS} ORDER BY ac.modified DESC, ac.id DESC LIMIT {TAKE} OFFSET {SKIP};";
        public const string LIST_BY_CONSUMER_AND_STATUS_PAGED = $@"SELECT ac.*, a.guid AS ack_guid, a.created AS ack_created FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE ac.consumer = {CONSUMER_ID} AND ac.status = {ACK_STATUS} ORDER BY ac.modified DESC, ac.id DESC LIMIT {TAKE} OFFSET {SKIP};";

        public const string LIST_READY_FOR_RETRY = $@"SELECT ac.*, a.guid AS ack_guid, a.created AS ack_created FROM ack_consumer ac JOIN ack a ON a.id = ac.ack_id WHERE ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN} ORDER BY ac.last_retry ASC, ac.id ASC;";

        public const string MARK_RETRY = $@"UPDATE ack_consumer SET retry_count = retry_count + 1, last_retry = CURRENT_TIMESTAMP WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID};";

        public const string SET_STATUS = $@"UPDATE ack_consumer SET status = {ACK_STATUS} WHERE ack_id = {ACK_ID} AND consumer = {CONSUMER_ID};";
        public const string SET_STATUS_BY_GUID = $@"UPDATE ack_consumer ac JOIN ack a ON a.id = ac.ack_id SET ac.status = {ACK_STATUS} WHERE a.guid = lower(trim({GUID})) AND ac.consumer = {CONSUMER_ID};";

        public const string DELETE = $@"DELETE FROM ack_consumer WHERE id = {ID};";
        public const string DELETE_BY_ACK_ID = $@"DELETE FROM ack_consumer WHERE ack_id = {ACK_ID};";
    }
    internal class QRY_ACK_HOOK {
        public const string EXISTS_BY_ACK_ID = $@"SELECT 1 FROM hook_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string EXISTS_BY_HOOK_ID = $@"SELECT 1 FROM hook_ack WHERE hook_id = {HOOK_ID} LIMIT 1;";

        public const string GET_BY_HOOK_ID = $@"SELECT * FROM hook_ack WHERE hook_id = {HOOK_ID} LIMIT 1;";
        public const string GET_BY_ACK_ID = $@"SELECT * FROM hook_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string GET_ACK_ID_BY_HOOK_ID = $@"SELECT ack_id AS id FROM hook_ack WHERE hook_id = {HOOK_ID} LIMIT 1;";

        // idempotent attach (hook_id is PK)
        public const string ATTACH = $@"INSERT INTO hook_ack (ack_id, hook_id) VALUES ({ACK_ID}, {HOOK_ID}) ON DUPLICATE KEY UPDATE ack_id = ack_id;";
        public const string DETACH = $@"DELETE FROM hook_ack WHERE hook_id = {HOOK_ID};";

        public const string DELETE_BY_ACK_ID = $@"DELETE FROM hook_ack WHERE ack_id = {ACK_ID};";
        public const string DELETE_BY_HOOK_ID = $@"DELETE FROM hook_ack WHERE hook_id = {HOOK_ID};";

        // Pending hook dispatch is now per consumer/status in ack_consumer
        public const string PENDING_ACKS = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, ac.consumer, ac.status, ac.last_retry, ac.retry_count, h.* FROM hook_ack ha JOIN ack a ON a.id = ha.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN hook h ON h.id = ha.hook_id WHERE ac.status = {ACK_STATUS} ORDER BY ac.last_retry ASC, ac.id ASC;";
    }
    internal class QRY_ACK_LC {
        public const string EXISTS_BY_ACK_ID = $@"SELECT 1 FROM lc_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string EXISTS_BY_LC_ID = $@"SELECT 1 FROM lc_ack WHERE lc_id = {LC_ID} LIMIT 1;";

        public const string GET_BY_LC_ID = $@"SELECT * FROM lc_ack WHERE lc_id = {LC_ID} LIMIT 1;";
        public const string GET_BY_ACK_ID = $@"SELECT * FROM lc_ack WHERE ack_id = {ACK_ID} LIMIT 1;";
        public const string GET_ACK_ID_BY_LC_ID = $@"SELECT ack_id AS id FROM lc_ack WHERE lc_id = {LC_ID} LIMIT 1;";

        // idempotent attach (lc_id is PK)
        public const string ATTACH = $@"INSERT INTO lc_ack (ack_id, lc_id) VALUES ({ACK_ID}, {LC_ID}) ON DUPLICATE KEY UPDATE ack_id = ack_id;";
        public const string DETACH = $@"DELETE FROM lc_ack WHERE lc_id = {LC_ID};";

        public const string DELETE_BY_ACK_ID = $@"DELETE FROM lc_ack WHERE ack_id = {ACK_ID};";
        public const string DELETE_BY_LC_ID = $@"DELETE FROM lc_ack WHERE lc_id = {LC_ID};";

        public const string PENDING_ACKS = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, ac.consumer, ac.status, ac.last_retry, ac.retry_count, l.* FROM lc_ack la JOIN ack a ON a.id = la.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id WHERE ac.status = {ACK_STATUS} ORDER BY ac.last_retry ASC, ac.id ASC;";
    }
    internal class QRY_ACK_DISPATCH {
        //LC ACK
        public const string LIST_PENDING_LC_READY = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.created AS ack_created, ac.id AS ack_consumer_id, ac.consumer, ac.status, ac.last_retry, ac.retry_count, ac.created AS consumer_created, ac.modified AS consumer_modified, l.id AS lc_id, l.instance_id, l.from_state, l.to_state, l.event, l.created AS lc_created, i.guid AS instance_guid, i.external_ref, d.actor, d.payload FROM lc_ack la JOIN ack a ON a.id = la.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id JOIN instance i ON i.id = l.instance_id LEFT JOIN lc_data d ON d.lc_id = l.id WHERE ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN} ORDER BY ac.last_retry ASC, ac.id ASC;";

        public const string LIST_PENDING_LC_READY_PAGED = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.created AS ack_created, ac.id AS ack_consumer_id, ac.consumer, ac.status, ac.last_retry, ac.retry_count, ac.created AS consumer_created, ac.modified AS consumer_modified, l.id AS lc_id, l.instance_id, i.def_version AS def_version_id, i.external_ref, l.from_state, l.to_state, l.event AS event_id, ev.code AS event_code, ev.display_name AS event_name, NULLIF(i.policy_id,0) AS policy_id, p.hash AS policy_hash, p.content AS policy_json, l.created AS lc_created, i.guid AS instance_guid, d.actor, d.payload FROM lc_ack la JOIN ack a ON a.id = la.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id JOIN instance i ON i.id = l.instance_id JOIN events ev ON ev.id = l.event LEFT JOIN policy p ON p.id = NULLIF(i.policy_id,0) LEFT JOIN lc_data d ON d.lc_id = l.id WHERE ac.consumer = {CONSUMER_ID} AND ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN} ORDER BY ac.last_retry ASC, ac.id ASC LIMIT {TAKE} OFFSET {SKIP};";

        public const string LIST_PENDING_LC_READY_BY_CONSUMER = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.created AS ack_created, ac.id AS ack_consumer_id, ac.consumer, ac.status, ac.last_retry, ac.retry_count, ac.created AS consumer_created, ac.modified AS consumer_modified, l.id AS lc_id, l.instance_id, i.def_version AS def_version_id, i.external_ref, l.from_state, l.to_state, l.event AS event_id, ev.code AS event_code, ev.display_name AS event_name, NULLIF(i.policy_id,0) AS policy_id, p.hash AS policy_hash, p.content AS policy_json, l.created AS lc_created, i.guid AS instance_guid, d.actor, d.payload FROM lc_ack la JOIN ack a ON a.id = la.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id JOIN instance i ON i.id = l.instance_id JOIN events ev ON ev.id = l.event LEFT JOIN policy p ON p.id = NULLIF(i.policy_id,0) LEFT JOIN lc_data d ON d.lc_id = l.id WHERE ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN} ORDER BY ac.last_retry ASC, ac.id ASC LIMIT {TAKE} OFFSET {SKIP};";

        public const string COUNT_PENDING_LC_READY = $@"SELECT COUNT(1) AS pending_count FROM lc_ack la JOIN ack_consumer ac ON ac.ack_id = la.ack_id WHERE ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN};";

        //HOOK ACK
        public const string LIST_PENDING_HOOK_READY = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.created AS ack_created, ac.id AS ack_consumer_id, ac.consumer, ac.status, ac.last_retry, ac.retry_count, ac.created AS consumer_created, ac.modified AS consumer_modified, h.id AS hook_id, h.instance_id, h.state_id, h.via_event, h.on_entry, h.route, h.created AS hook_created, i.guid AS instance_guid, i.external_ref FROM hook_ack ha JOIN ack a ON a.id = ha.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN hook h ON h.id = ha.hook_id JOIN instance i ON i.id = h.instance_id WHERE ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN} ORDER BY ac.last_retry ASC, ac.id ASC;";

        public const string LIST_PENDING_HOOK_READY_PAGED = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.created AS ack_created, ac.id AS ack_consumer_id, ac.consumer AS consumer, ac.status AS status, ac.last_retry AS last_retry, ac.retry_count AS retry_count, ac.created AS consumer_created, ac.modified AS consumer_modified, ha.hook_id AS hook_id, h.instance_id AS instance_id, i.def_version AS def_version_id, i.external_ref AS external_ref, h.state_id AS state_id, h.via_event AS via_event, h.on_entry AS on_entry, h.route AS route, h.created AS hook_created FROM hook_ack ha JOIN ack a ON a.id = ha.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN hook h ON h.id = ha.hook_id JOIN instance i ON i.id = h.instance_id WHERE ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN} ORDER BY ac.last_retry ASC, ac.id ASC LIMIT {TAKE} OFFSET {SKIP};";

        public const string LIST_PENDING_HOOK_READY_BY_CONSUMER = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.created AS ack_created, ac.id AS ack_consumer_id, ac.consumer AS consumer, ac.status AS status, ac.last_retry AS last_retry, ac.retry_count AS retry_count, ac.created AS consumer_created, ac.modified AS consumer_modified, ha.hook_id AS hook_id, h.instance_id AS instance_id, i.def_version AS def_version_id, i.external_ref AS external_ref, h.state_id AS state_id, h.via_event AS via_event, h.on_entry AS on_entry, h.route AS route, h.created AS hook_created FROM hook_ack ha JOIN ack a ON a.id = ha.ack_id JOIN ack_consumer ac ON ac.ack_id = a.id JOIN hook h ON h.id = ha.hook_id JOIN instance i ON i.id = h.instance_id WHERE ac.consumer = {CONSUMER_ID} AND ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN} ORDER BY ac.last_retry ASC, ac.id ASC LIMIT {TAKE} OFFSET {SKIP};";

        public const string COUNT_PENDING_HOOK_READY = $@"SELECT COUNT(1) AS pending_count FROM hook_ack ha JOIN ack_consumer ac ON ac.ack_id = ha.ack_id WHERE ac.status = {ACK_STATUS} AND ac.last_retry < {OLDER_THAN};";
    }
}
