using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_STATE {
        public const string INSERT = $"INSERT IGNORE INTO state (display_name, flags, category, def_version, timeout, timeout_mode, timeout_event) VALUES ({DISPLAY_NAME}, {FLAGS}, {CATEGORY}, {DEF_VERSION}, {TIMEOUT}, {TIMEOUT_MODE}, {TIMEOUT_EVENT}); SELECT * FROM state WHERE def_version = {DEF_VERSION} AND name = lower({DISPLAY_NAME}) LIMIT 1;";
        public const string GET_BY_ID = $"SELECT * FROM state WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_VERSION = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} ORDER BY id;";
        public const string GET_BY_VERSION_FULL = $"SELECT s.*, c.display_name AS category_display_name, c.name AS category_name, e.code AS timeout_event_code, e.display_name AS timeout_event_display_name, e.name AS timeout_event_name FROM state s INNER JOIN category c ON c.id = s.category LEFT JOIN events e ON e.id = s.timeout_event WHERE s.def_version = {DEF_VERSION} ORDER BY s.id;";
        public const string GET_BY_NAME = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND name = lower({NAME}) LIMIT 1;";
        public const string GET_INITIAL = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 1) = 1 LIMIT 1;";
        public const string GET_FINAL = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 2) = 2 LIMIT 1;";
        public const string UPDATE = $"UPDATE state SET display_name = {DISPLAY_NAME}, flags = {FLAGS}, category = {CATEGORY}, timeout = {TIMEOUT}, timeout_mode = {TIMEOUT_MODE}, timeout_event = {TIMEOUT_EVENT} WHERE id = {ID};";
        public const string UPDATE_FLAGS = $"UPDATE state SET flags = {FLAGS} WHERE id = {ID};";
        public const string DELETE = $"DELETE FROM state WHERE id = {ID};";
    }
}