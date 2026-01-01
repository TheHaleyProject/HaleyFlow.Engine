using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_ACK_DISPATCH {
        //LIFECYCLE DISPATCH

        public const string LIST_PENDING_LC_READY = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.consumer, a.source, a.ack_status, a.last_retry, a.retry_count, a.created AS ack_created, a.modified AS ack_modified, l.id AS lc_id, l.instance_id, l.from_state, l.to_state, l.event, l.created AS lc_created, i.guid AS instance_guid, i.external_ref, d.actor, d.payload FROM ack a JOIN lc_ack la ON la.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id JOIN instance i ON i.id = l.instance_id LEFT JOIN lc_data d ON d.lc_id = l.id WHERE a.ack_status = {ACK_STATUS} AND (a.last_retry IS NULL OR a.last_retry < {OLDER_THAN}) ORDER BY a.last_retry ASC, a.id ASC;";

        public const string LIST_PENDING_LC_READY_PAGED = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.consumer, a.source, a.ack_status, a.last_retry, a.retry_count, a.created AS ack_created, a.modified AS ack_modified, l.id AS lc_id, l.instance_id, l.from_state, l.to_state, l.event, l.created AS lc_created, i.guid AS instance_guid, i.external_ref, d.actor, d.payload FROM ack a JOIN lc_ack la ON la.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id JOIN instance i ON i.id = l.instance_id LEFT JOIN lc_data d ON d.lc_id = l.id WHERE a.ack_status = {ACK_STATUS} AND (a.last_retry IS NULL OR a.last_retry < {OLDER_THAN}) ORDER BY a.last_retry ASC, a.id ASC LIMIT {TAKE} OFFSET {SKIP};";

        public const string LIST_PENDING_LC_READY_BY_CONSUMER = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.consumer, a.source, a.ack_status, a.last_retry, a.retry_count, a.created AS ack_created, a.modified AS ack_modified, l.id AS lc_id, l.instance_id, l.from_state, l.to_state, l.event, l.created AS lc_created, i.guid AS instance_guid, i.external_ref, d.actor, d.payload FROM ack a JOIN lc_ack la ON la.ack_id = a.id JOIN lifecycle l ON l.id = la.lc_id JOIN instance i ON i.id = l.instance_id LEFT JOIN lc_data d ON d.lc_id = l.id WHERE a.consumer = {CONSUMER} AND a.ack_status = {ACK_STATUS} AND (a.last_retry IS NULL OR a.last_retry < {OLDER_THAN}) ORDER BY a.last_retry ASC, a.id ASC;";

        public const string COUNT_PENDING_LC_READY = $@"SELECT COUNT(1) AS pending_count FROM ack a JOIN lc_ack la ON la.ack_id = a.id WHERE a.ack_status = {ACK_STATUS} AND (a.last_retry IS NULL OR a.last_retry < {OLDER_THAN});";

       //HOOK DISPATCH
        public const string LIST_PENDING_HOOK_READY = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.consumer, a.source, a.ack_status, a.last_retry, a.retry_count, a.created AS ack_created, a.modified AS ack_modified, h.id AS hook_id, h.instance_id, h.state_id, h.via_event, h.on_entry, h.route, h.created AS hook_created, i.guid AS instance_guid, i.external_ref FROM ack a JOIN hook_ack ha ON ha.ack_id = a.id JOIN hook h ON h.id = ha.hook_id JOIN instance i ON i.id = h.instance_id WHERE a.ack_status = {ACK_STATUS} AND (a.last_retry IS NULL OR a.last_retry < {OLDER_THAN}) ORDER BY a.last_retry ASC, a.id ASC;";

        public const string LIST_PENDING_HOOK_READY_PAGED = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.consumer, a.source, a.ack_status, a.last_retry, a.retry_count, a.created AS ack_created, a.modified AS ack_modified, h.id AS hook_id, h.instance_id, h.state_id, h.via_event, h.on_entry, h.route, h.created AS hook_created, i.guid AS instance_guid, i.external_ref FROM ack a JOIN hook_ack ha ON ha.ack_id = a.id JOIN hook h ON h.id = ha.hook_id JOIN instance i ON i.id = h.instance_id WHERE a.ack_status = {ACK_STATUS} AND (a.last_retry IS NULL OR a.last_retry < {OLDER_THAN}) ORDER BY a.last_retry ASC, a.id ASC LIMIT {TAKE} OFFSET {SKIP};";

        public const string LIST_PENDING_HOOK_READY_BY_CONSUMER = $@"SELECT a.id AS ack_id, a.guid AS ack_guid, a.consumer, a.source, a.ack_status, a.last_retry, a.retry_count, a.created AS ack_created, a.modified AS ack_modified, h.id AS hook_id, h.instance_id, h.state_id, h.via_event, h.on_entry, h.route, h.created AS hook_created, i.guid AS instance_guid, i.external_ref FROM ack a JOIN hook_ack ha ON ha.ack_id = a.id JOIN hook h ON h.id = ha.hook_id JOIN instance i ON i.id = h.instance_id WHERE a.consumer = {CONSUMER} AND a.ack_status = {ACK_STATUS} AND (a.last_retry IS NULL OR a.last_retry < {OLDER_THAN}) ORDER BY a.last_retry ASC, a.id ASC;";

        public const string COUNT_PENDING_HOOK_READY = $@"SELECT COUNT(1) AS pending_count FROM ack a JOIN hook_ack ha ON ha.ack_id = a.id WHERE a.ack_status = {ACK_STATUS} AND (a.last_retry IS NULL OR a.last_retry < {OLDER_THAN});";
    }
}
