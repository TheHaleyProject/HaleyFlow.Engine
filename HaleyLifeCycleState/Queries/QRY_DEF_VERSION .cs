using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_DEF_VERSION {
        public const string INSERT = $@"INSERT IGNORE INTO def_version (parent, version, data) VALUES ({PARENT}, {VERSION}, {DATA}); SELECT * FROM def_version WHERE parent = {PARENT} AND version = {VERSION} LIMIT 1;";
        public const string GET_BY_ID = $@"SELECT * FROM def_version WHERE id = {ID} LIMIT 1;";
        public const string GET_BY_GUID = $@"SELECT * FROM def_version WHERE guid = {GUID} LIMIT 1;";
        public const string GET_BY_PARENT = $@"SELECT * FROM def_version WHERE parent = {PARENT} ORDER BY version;";
        public const string GET_LATEST = $@"SELECT * FROM def_version WHERE parent = {PARENT} ORDER BY version DESC LIMIT 1;";
        public const string UPDATE_DATA = $@"UPDATE def_version SET data = {DATA}, modified = utc_timestamp() WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM def_version WHERE id = {ID};";

    }
}
