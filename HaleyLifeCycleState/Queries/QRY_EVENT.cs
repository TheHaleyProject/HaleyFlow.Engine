using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_EVENT {
        public const string INSERT = $@"INSERT IGNORE INTO events (display_name, def_version) VALUES ({DISPLAY_NAME}, {DEF_VERSION}); SELECT * FROM events WHERE def_version = {DEF_VERSION} AND name = lower({DISPLAY_NAME}) LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM events WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_VERSION = $@"SELECT * FROM events WHERE def_version = {DEF_VERSION} ORDER BY id;";
        public const string GET_BY_NAME = $@"SELECT * FROM events WHERE def_version = {DEF_VERSION} AND name = lower({NAME}) LIMIT 1;";
        public const string DELETE = $@"DELETE FROM events WHERE id = {ID};";
    }
}
