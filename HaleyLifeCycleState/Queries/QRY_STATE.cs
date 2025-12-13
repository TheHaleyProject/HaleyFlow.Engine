using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_STATE {
        public const string INSERT = $"INSERT IGNORE INTO state (display_name, flags, category, def_version, timeout_minutes, timeout_mode, timeout_event) VALUES (trim({DISPLAY_NAME}), {FLAGS}, {CATEGORY}, {DEF_VERSION}, {TIMEOUT_MINUTES}, {TIMEOUT_MODE}, {TIMEOUT_EVENT}); SELECT * FROM state WHERE def_version = {DEF_VERSION} AND name = lower(TRIM({DISPLAY_NAME})) LIMIT 1;";

        public const string UPDATE = $"UPDATE state SET display_name=COALESCE({DISPLAY_NAME},display_name), flags=COALESCE({FLAGS},flags), category=COALESCE({CATEGORY},category), timeout_minutes=COALESCE({TIMEOUT_MINUTES},timeout_minutes), timeout_mode=COALESCE({TIMEOUT_MODE},timeout_mode), timeout_event=COALESCE({TIMEOUT_EVENT},timeout_event) WHERE id={ID};";

        public const string GET_BY_ID = $"SELECT * FROM state WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_VERSION = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} ORDER BY id;";
        public const string GET_BY_VERSION_WITH_CATEGORY = $"SELECT s.*, c.display_name AS category_display_name, e.code AS timeout_event_code, e.display_name AS timeout_event_display_name FROM state s LEFT JOIN category c ON c.id = s.category LEFT JOIN events e ON e.id = s.timeout_event WHERE s.def_version = {DEF_VERSION} ORDER BY s.id;";
        public const string GET_BY_NAME = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND name = lower(trim({NAME})) LIMIT 1;";

        //public const string GET_INITIAL = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 1) = 1 LIMIT 1;";
        //public const string GET_FINAL = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 2) = 2;";
        //public const string GET_SYSTEM = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 4) = 4;";
        //public const string GET_ERROR = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 8) = 8;";
        public const string GET_BY_FLAGS = $"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND ((flags & {FLAGS}) = {FLAGS});"; //No semicolon at the end intentionally.
        public const string UPDATE_FLAGS = $"UPDATE state SET flags = {FLAGS} WHERE id = {ID};";
        public const string DELETE = $"DELETE FROM state WHERE id = {ID};";

        public const string EXISTS_BY_ID = $@"SELECT 1 FROM state WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_VERSION_AND_NAME = $@"SELECT 1 FROM state WHERE def_version = {DEF_VERSION} AND name = lower(trim({NAME})) LIMIT 1;";
    }
}