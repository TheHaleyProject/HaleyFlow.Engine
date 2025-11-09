using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

internal class QRY_STATE {
    public const string INSERT = $@"INSERT IGNORE INTO state (display_name, flags, category, def_version) VALUES ({DISPLAY_NAME}, {FLAGS}, {CATEGORY}, {DEF_VERSION}); SELECT id FROM state WHERE display_name = {DISPLAY_NAME} AND def_version = {DEF_VERSION} LIMIT 1;";
    public const string GET_BY_ID = $@"SELECT * FROM state WHERE id = {ID};";
    public const string GET_BY_VERSION = $@"SELECT * FROM state WHERE def_version = {DEF_VERSION};";
    public const string GET_BY_NAME = $@"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND name = {NAME} LIMIT 1;";
    public const string GET_INITIAL = $@"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 1) = 1 LIMIT 1;";
    public const string GET_FINAL = $@"SELECT * FROM state WHERE def_version = {DEF_VERSION} AND (flags & 2) = 2 LIMIT 1;";
    public const string UPDATE_FLAGS = $@"UPDATE state SET flags = {FLAGS} WHERE id = {ID};";
    public const string DELETE = $@"DELETE FROM state WHERE id = {ID};";
}
