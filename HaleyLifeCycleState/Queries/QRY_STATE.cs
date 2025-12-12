using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_STATE {
        public const string INSERT = $@"INSERT IGNORE INTO state (display_name, flags, category, def_version) VALUES ({DISPLAY_NAME}, {FLAGS}, {CATEGORY}, {DEF_VERSION}); SELECT * FROM state WHERE def_version = {DEF_VERSION} AND name = lower({DISPLAY_NAME}) LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM state WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_VERSION = $@"SELECT * FROM state WHERE def_version = {DEF_VERSION} ORDER BY id;";
        public const string GET_BY_VERSION_WITH_CATEGORY = $@"SELECT s.*, c.display_name AS category_display_name FROM state s LEFT JOIN category c ON c.id = s.category WHERE s.def_version = {DEF_VERSION} ORDER BY s.id;";
        public const string GET_BY_NAME = $@"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND name = lower({NAME}) LIMIT 1;";
        public const string GET_INITIAL = $@"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 1) = 1 LIMIT 1;";
        public const string GET_FINAL = $@"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 2) = 2 LIMIT 1;";
        public const string UPDATE_FLAGS = $@"UPDATE state SET flags = {FLAGS} WHERE id = {ID};";
        public const string UPDATE = $@"UPDATE state SET display_name = {DISPLAY_NAME}, flags = {FLAGS}, category = {CATEGORY} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM state WHERE id = {ID};";
    }
}