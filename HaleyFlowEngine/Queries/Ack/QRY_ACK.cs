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
}
