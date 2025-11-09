using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_TRANSITION_LOG {
        public const string INSERT = $@"INSERT INTO transition_log (instance_id, from_state, to_state, event, actor, flags, metadata) VALUES ({INSTANCE_ID}, {FROM_STATE}, {TO_STATE}, {EVENT}, {ACTOR}, {FLAGS}, {METADATA}); SELECT LAST_INSERT_ID();";
        public const string GET_BY_INSTANCE = $@"SELECT * FROM transition_log WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC;";
        public const string GET_BY_STATE_CHANGE = $@"SELECT * FROM transition_log WHERE from_state = {FROM_STATE} AND to_state = {TO_STATE};";
        public const string GET_BY_DATE_RANGE = $@"SELECT * FROM transition_log WHERE created BETWEEN {CREATED} AND {MODIFIED} ORDER BY created;";
        public const string GET_LATEST_FOR_INSTANCE = $@"SELECT * FROM transition_log WHERE instance_id = {INSTANCE_ID} ORDER BY created DESC LIMIT 1;";
    }
}
