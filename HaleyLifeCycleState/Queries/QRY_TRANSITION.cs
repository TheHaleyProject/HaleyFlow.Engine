using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_TRANSITION {
        public const string INSERT = $"INSERT IGNORE INTO transition (from_state, to_state, event, def_version) VALUES ({FROM_STATE}, {TO_STATE}, {EVENT}, {DEF_VERSION}); SELECT * FROM transition WHERE def_version = {DEF_VERSION} AND from_state = {FROM_STATE} AND to_state = {TO_STATE} AND event = {EVENT} LIMIT 1;";
        public const string GET_BY_ID = $"SELECT * FROM transition WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_VERSION = $"SELECT * FROM transition WHERE def_version = {DEF_VERSION} ORDER BY id;";
        public const string GET_BY_UNQ = $"SELECT * FROM transition WHERE def_version = {DEF_VERSION} AND from_state = {FROM_STATE} AND to_state = {TO_STATE} AND event = {EVENT} LIMIT 1;";

        //For a given event,from a starting state, transition can be towards only one state.
        public const string GET_TRANSITION = $"SELECT * FROM transition WHERE def_version = {DEF_VERSION} AND from_state = {FROM_STATE} AND event = {EVENT} ORDER BY id LIMIT 1;";

        //A given state can have multiple outgoing transitions.
        public const string GET_OUTGOING = $"SELECT * FROM transition WHERE def_version = {DEF_VERSION} AND from_state = {FROM_STATE} ORDER BY id;";
        public const string DELETE = $"DELETE FROM transition WHERE id = {ID};";

        public const string EXISTS_BY_ID = $@"SELECT 1 FROM transition WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_UNQ = $@"SELECT 1 FROM transition WHERE def_version = {DEF_VERSION} AND from_state = {FROM_STATE} AND to_state = {TO_STATE} AND event = {EVENT} LIMIT 1;";

    }
}
