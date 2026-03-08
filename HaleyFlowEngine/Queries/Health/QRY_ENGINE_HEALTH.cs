using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal static class QRY_ENGINE_HEALTH {
        public const string COUNT_CONSUMERS_TOTAL = @"SELECT COUNT(*) AS cnt FROM consumer;";
        public const string COUNT_CONSUMERS_ALIVE = $@"SELECT COUNT(*) AS cnt FROM consumer WHERE TIMESTAMPDIFF(SECOND, last_beat, UTC_TIMESTAMP()) <= {TTL_SECONDS};";
        public const string COUNT_CONSUMERS_DOWN = $@"SELECT COUNT(*) AS cnt FROM consumer WHERE TIMESTAMPDIFF(SECOND, last_beat, UTC_TIMESTAMP()) > {TTL_SECONDS};";

        public const string COUNT_STALE_DEFAULT_STATE = $@"SELECT COUNT(*) AS cnt FROM instance i JOIN state s ON s.id = i.current_state AND s.def_version = i.def_version JOIN lifecycle l ON l.id = (SELECT l2.id FROM lifecycle l2 WHERE l2.instance_id = i.id AND l2.to_state = i.current_state ORDER BY l2.id DESC LIMIT 1) WHERE (i.flags & {FLAGS}) = 0 AND NOT EXISTS (SELECT 1 FROM timeouts tm WHERE tm.policy_id = i.policy_id AND tm.state_name = s.name AND tm.event_code IS NOT NULL) AND l.created <= DATE_SUB(UTC_TIMESTAMP(), INTERVAL {STALE_SECONDS} SECOND) AND NOT EXISTS (SELECT 1 FROM lc_ack la JOIN ack_consumer ac ON ac.ack_id = la.ack_id WHERE la.lc_id = l.id AND ac.status <> {ACK_STATUS}) AND NOT EXISTS (SELECT 1 FROM hook h JOIN hook_ack ha ON ha.hook_id = h.id JOIN ack_consumer ac2 ON ac2.ack_id = ha.ack_id WHERE h.instance_id = i.id AND h.state_id = i.current_state AND ac2.status <> {ACK_STATUS});";
    }
}
