using System.Diagnostics;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_LC_DATA {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM lc_data WHERE lc_id = {ID} LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT lc_id, actor, payload FROM lc_data WHERE lc_id = {ID} LIMIT 1;";
        public const string UPSERT = $@"INSERT INTO lc_data (lc_id, actor, payload) VALUES ({ID}, {ACTOR}, {PAYLOAD}) ON DUPLICATE KEY UPDATE actor = VALUES(actor), payload = VALUES(payload);";
        public const string DELETE = $@"DELETE FROM lc_data WHERE lc_id = {ID};";
    }
}
