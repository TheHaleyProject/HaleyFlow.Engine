using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_TRANSITION_DATA {
        public const string UPSERT = $@"INSERT INTO transition_data (transition_log, actor, metadata) VALUES ({TRANSITION_LOG}, {ACTOR}, {METADATA}) ON DUPLICATE KEY UPDATE actor = VALUES(actor), metadata = VALUES(metadata);";
        public const string GET_BY_LOG = $@"SELECT * FROM transition_data WHERE transition_log = {TRANSITION_LOG} LIMIT 1;";
        public const string DELETE_BY_LOG = $@"DELETE FROM transition_data WHERE transition_log = {TRANSITION_LOG};";
    }
}
