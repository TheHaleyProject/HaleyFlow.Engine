using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_TRANSITION {
        public const string INSERT = $@"INSERT IGNORE INTO transition (from_state, to_state, event, flags, guard_condition, def_version) VALUES ({FROM_STATE}, {TO_STATE}, {EVENT}, {FLAGS}, {GUARD_CONDITION}, {DEF_VERSION}); SELECT id FROM transition WHERE from_state = {FROM_STATE} AND to_state = {TO_STATE} AND event = {EVENT} AND def_version = {DEF_VERSION} LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM transition WHERE id = {ID};";
        public const string GET_BY_VERSION = $@"SELECT * FROM transition WHERE def_version = {DEF_VERSION};";
        public const string GET_TRANSITION = $@"SELECT * FROM transition WHERE from_state = {FROM_STATE} AND event = {EVENT} AND def_version = {DEF_VERSION} LIMIT 1;";
        public const string GET_OUTGOING = $@"SELECT * FROM transition WHERE from_state = {FROM_STATE} AND def_version = {DEF_VERSION};";
        public const string DELETE = $@"DELETE FROM transition WHERE id = {ID};";
    }
}
