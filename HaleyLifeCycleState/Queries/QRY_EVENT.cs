using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_EVENT {
        public const string INSERT = $@"INSERT IGNORE INTO events (display_name, def_version) VALUES ({DISPLAY_NAME}, {DEF_VERSION}); SELECT id FROM events WHERE display_name = {DISPLAY_NAME} AND def_version = {DEF_VERSION} LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM events WHERE id = {ID};";
        public const string GET_BY_VERSION = $@"SELECT * FROM events WHERE def_version = {DEF_VERSION};";
        public const string GET_BY_NAME = $@"SELECT * FROM events WHERE def_version = {DEF_VERSION} AND name = {NAME} LIMIT 1;";
        public const string DELETE = $@"DELETE FROM events WHERE id = {ID};";
    }
}
