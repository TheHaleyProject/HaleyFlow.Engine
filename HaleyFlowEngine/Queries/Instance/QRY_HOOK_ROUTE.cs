using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_HOOK_ROUTE {
        // Global lookup: one row per unique route string (e.g. "APP.PREQ.EVAL.TECHNICAL.REVIEW").
        // hook.route_id FK points here instead of storing the raw string on every hook row.

        public const string GET_ID_BY_NAME = $@"SELECT id FROM hook_route WHERE name = {ROUTE} LIMIT 1;";

        public const string INSERT = $@"INSERT INTO hook_route (name) VALUES ({ROUTE}); SELECT LAST_INSERT_ID() AS id;";
    }
}
