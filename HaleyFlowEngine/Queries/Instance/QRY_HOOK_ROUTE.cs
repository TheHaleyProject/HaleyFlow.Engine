using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_HOOK_ROUTE {
        // Global lookup: one row per unique route string (e.g. "APP.PREQ.EVAL.TECHNICAL.REVIEW").
        // hook.route_id FK points here instead of storing the raw string on every hook row.

        public const string GET_ID_BY_NAME = $@"SELECT id FROM hook_route WHERE name = {ROUTE} LIMIT 1;";

        public const string INSERT = $@"INSERT INTO hook_route (name, label) VALUES ({ROUTE}, ''); SELECT LAST_INSERT_ID() AS id;";

        // Upsert label for a known route name. Inserts if not present; updates label if it exists.
        public const string UPSERT_LABEL = $@"INSERT INTO hook_route (name, label) VALUES ({ROUTE}, {LABEL}) ON DUPLICATE KEY UPDATE label = VALUES(label);";
    }
}
