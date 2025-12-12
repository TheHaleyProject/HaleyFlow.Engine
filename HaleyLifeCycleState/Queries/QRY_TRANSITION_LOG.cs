using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_TRANSITION_LOG {
        public const string INSERT = $"INSERT INTO transition_log (instance_id, from_state, to_state, event, flags) VALUES ({INSTANCE_ID}, {FROM_STATE}, {TO_STATE}, {EVENT}, {FLAGS}); SELECT LAST_INSERT_ID();";
        public const string GET_BY_INSTANCE = $"SELECT tl.*, td.actor, td.metadata FROM transition_log tl LEFT JOIN transition_data td ON td.transition_log = tl.id WHERE tl.instance_id = {INSTANCE_ID} ORDER BY tl.created DESC;";
        public const string GET_BY_STATE_CHANGE = $"SELECT tl.*, td.actor, td.metadata FROM transition_log tl LEFT JOIN transition_data td ON td.transition_log = tl.id WHERE tl.from_state = {FROM_STATE} AND tl.to_state = {TO_STATE} ORDER BY tl.created DESC;";
        public const string GET_BY_DATE_RANGE = $"SELECT tl.*, td.actor, td.metadata FROM transition_log tl LEFT JOIN transition_data td ON td.transition_log = tl.id WHERE tl.created BETWEEN {CREATED} AND {MODIFIED} ORDER BY tl.created;";
        public const string GET_LATEST_FOR_INSTANCE = $"SELECT tl.*, td.actor, td.metadata FROM transition_log tl LEFT JOIN transition_data td ON td.transition_log = tl.id WHERE tl.instance_id = {INSTANCE_ID} ORDER BY tl.created DESC LIMIT 1;";
    }
}
