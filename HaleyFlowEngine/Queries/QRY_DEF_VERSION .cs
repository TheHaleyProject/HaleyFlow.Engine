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
        public const string GET_BY_PARENT_VERSION =  $"SELECT * FROM def_version WHERE parent = {PARENT} AND version = {VERSION} LIMIT 1;";
        public const string GET_LATEST = $@"SELECT * FROM def_version WHERE parent = {PARENT} ORDER BY version DESC LIMIT 1;";
        public const string UPDATE_DATA = $@"UPDATE def_version SET data = {DATA} WHERE id = {ID};";
        public const string DELETE = $@"DELETE FROM def_version WHERE id = {ID};";
        public const string GET_LATEST_BY_ENV = $@"SELECT dv.*, e.code as code, e.name as env_name, d.name as def_name, d.guid as def_guid FROM def_version dv INNER JOIN definition d ON d.id = dv.parent INNER JOIN environment e ON e.id = d.env WHERE e.code = {CODE} AND d.name = lower(trim({NAME})) ORDER BY dv.version DESC LIMIT 1;";

        public const string EXISTS_BY_ID = $@"SELECT 1 FROM def_version WHERE id = {ID} LIMIT 1;";
        public const string EXISTS_BY_GUID = $@"SELECT 1 FROM def_version WHERE guid = {GUID} LIMIT 1;";
        public const string EXISTS_BY_PARENT_AND_VERSION =$@"SELECT 1 FROM def_version WHERE parent = {PARENT} AND version = {VERSION} LIMIT 1;";

    }
}
