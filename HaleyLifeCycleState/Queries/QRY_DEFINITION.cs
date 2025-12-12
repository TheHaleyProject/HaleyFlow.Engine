using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_DEFINITION {
        public const string INSERT = $@"INSERT IGNORE INTO definition (display_name, description, env) VALUES ({DISPLAY_NAME}, {DESCRIPTION}, {ENV}); SELECT * FROM definition WHERE env = {ENV} AND name = lower({DISPLAY_NAME}) LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM definition WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT * FROM definition WHERE guid = {GUID} LIMIT 1;";
        public const string GET_BY_NAME = $@"SELECT * FROM definition WHERE env = {ENV} AND name = lower({NAME}) LIMIT 1;";
        public const string GET_ALL = $@"SELECT * FROM definition ORDER BY env, display_name;";
        public const string UPDATE_DESCRIPTION = $@"UPDATE definition SET description = {DESCRIPTION} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM definition WHERE id = {ID};";
    }
}
