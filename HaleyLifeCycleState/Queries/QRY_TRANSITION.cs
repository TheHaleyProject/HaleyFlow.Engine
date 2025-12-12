using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_TRANSITION {
        public const string INSERT = $@"INSERT IGNORE INTO transition (from_state, to_state, event, flags, guard_key, def_version) VALUES ({FROM_STATE}, {TO_STATE}, {EVENT}, {FLAGS}, {GUARD_KEY}, {DEF_VERSION}); SELECT * FROM transition WHERE def_version = {DEF_VERSION} AND from_state = {FROM_STATE} AND to_state = {TO_STATE} AND event = {EVENT} LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM transition WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_VERSION = $@"SELECT * FROM transition WHERE def_version = {DEF_VERSION} ORDER BY id;";
        public const string GET_TRANSITION = $@"SELECT * FROM transition WHERE def_version = {DEF_VERSION} AND from_state = {FROM_STATE} AND event = {EVENT} LIMIT 1;";
        public const string GET_OUTGOING = $@"SELECT * FROM transition WHERE def_version = {DEF_VERSION} AND from_state = {FROM_STATE} ORDER BY id;";
        public const string DELETE = $@"DELETE FROM transition WHERE id = {ID};";

    }
}
