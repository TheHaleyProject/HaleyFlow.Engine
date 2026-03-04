using System.Diagnostics.Tracing;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal static class QRY_CONSUMER {
        public const string GET_ID_BY_ENV_ID_AND_GUID = @$"SELECT id FROM consumer WHERE env={ENV_ID} AND consumer_guid=lower(trim({CONSUMER_GUID})) LIMIT 1;";
        public const string INSERT = @$"INSERT INTO consumer(env, consumer_guid, last_beat) VALUES({ENV_ID}, lower(trim({CONSUMER_GUID})), CURRENT_TIMESTAMP);";
        public const string UPDATE_BEAT_BY_ID = @$"UPDATE consumer SET last_beat=CURRENT_TIMESTAMP WHERE id={CONSUMER_ID};";
        public const string LIST_ALIVE_IDS_BY_ENV_ID = @$"SELECT id FROM consumer WHERE env={ENV_ID} AND TIMESTAMPDIFF(SECOND, last_beat, CURRENT_TIMESTAMP) <= {TTL_SECONDS};";
        public const string IS_ALIVE_BY_ID = @$"SELECT CASE WHEN TIMESTAMPDIFF(SECOND, last_beat, CURRENT_TIMESTAMP) <= {TTL_SECONDS} THEN 1 ELSE 0 END AS alive FROM consumer WHERE id={CONSUMER_ID};";

        public const string IS_ALIVE_BY_ENV_ID_AND_GUID = @$"SELECT CASE WHEN TIMESTAMPDIFF(SECOND, last_beat, CURRENT_TIMESTAMP) <= {TTL_SECONDS} THEN 1 ELSE 0 END AS alive FROM consumer WHERE env={ENV_ID} AND consumer_guid=lower(trim({CONSUMER_GUID})) LIMIT 1;";
        public const string GET_ID_ALIVE_BY_ENV_ID_AND_GUID = @$"SELECT id FROM consumer WHERE env={ENV_ID} AND consumer_guid=lower(trim({CONSUMER_GUID})) AND TIMESTAMPDIFF(SECOND, last_beat, CURRENT_TIMESTAMP) <= {TTL_SECONDS} LIMIT 1;";
        public const string GET_LAST_BEAT_BY_ENV_ID_AND_GUID = @$"SELECT last_beat FROM consumer WHERE env={ENV_ID} AND consumer_guid=lower(trim({CONSUMER_GUID})) LIMIT 1;";
        public const string UPSERT_BEAT_BY_ENV_ID_AND_GUID = @$"INSERT INTO consumer(env, consumer_guid, last_beat) VALUES({ENV_ID}, lower(trim({CONSUMER_GUID})), CURRENT_TIMESTAMP) ON DUPLICATE KEY UPDATE last_beat=CURRENT_TIMESTAMP;";
        public const string UPDATE_BEAT_BY_ENV_ID_AND_GUID = @$"UPDATE consumer SET last_beat=CURRENT_TIMESTAMP WHERE env={ENV_ID} AND consumer_guid=lower(trim({CONSUMER_GUID}));";

    }
}
