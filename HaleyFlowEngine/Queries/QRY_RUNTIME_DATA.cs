using System.Diagnostics;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_RUNTIME_DATA {
        public const string EXISTS_BY_ID = $@"SELECT 1 FROM runtime_data WHERE runtime = {ID} LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT runtime, data, payload FROM runtime_data WHERE runtime = {ID} LIMIT 1;";
        public const string UPSERT = $@"INSERT INTO runtime_data (runtime, data, payload) VALUES ({ID}, {DATA}, {PAYLOAD}) ON DUPLICATE KEY UPDATE data = VALUES(data), payload = VALUES(payload);";
        public const string DELETE = $@"DELETE FROM runtime_data WHERE runtime = {ID};";
    }
}
