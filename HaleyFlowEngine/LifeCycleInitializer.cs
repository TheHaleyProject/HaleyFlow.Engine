using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace Haley {
    public class LifeCycleInitializer {
        const string FALLBACK_DB_NAME = "hdb_lc_state";
        const string EMBEDDED_SQL_RESOURCE = "Haley.Scripts.lc_state.sql";
        const string REPLACE_DBNAME = "lcstate";
        public static async Task<IFeedback<string>> InitializeAsyncWithConString(IAdapterGateway agw,  string connectionstring) {
            var result = new Feedback<string>();
            var adapterKey = RandomUtils.GetString(128).SanitizeBase64();
            agw.Add(new AdapterConfig() { 
                AdapterKey = adapterKey,
                ConnectionString = connectionstring,
                DBType = TargetDB.maria
            });
            var fb = await InitializeAsync(agw, adapterKey);
            return result.SetStatus(fb.Status).SetResult(adapterKey);
        }

        public static Task<IFeedback> InitializeAsync(IAdapterGateway agw, string adapterKey) {
            //var toReplace = new Dictionary<string, string> { ["lifecycle_state"] = }
            return agw.CreateDatabase(new DbCreationArgs(adapterKey) {
                ContentProcessor = (content, dbname) => {
                    //Custom processor to set the DB name in the SQL content.
                    return content.Replace(REPLACE_DBNAME, dbname);
                },
                FallBackDBName = FALLBACK_DB_NAME,
                SQLContent = Encoding.UTF8.GetString(ResourceUtils.GetEmbeddedResource(EMBEDDED_SQL_RESOURCE))
            });

            //if (!(dirInfo is IStorageModule info)) return;
            //if (string.IsNullOrWhiteSpace(info.DatabaseName)) info.DatabaseName = $@"{DB_MODULE_NAME_PREFIX}{info.Cuid}";
            ////What if the CUID is changed? Should we use the guid instead? 
            ////But, guid is not unique across clients. So, we use cuid.
            ////So, when we create the module, we use the cuid as the database name.
            ////TODO : IF A CUID IS CHANGED, THEN WE NEED TO UPDATE THE DATABASE NAME IN THE DB.
            //var sqlFile = Path.Combine(AssemblyUtils.GetBaseDirectory(), DB_SQL_FILE_LOCATION, DB_CLIENT_SQL_FILE);
            //if (!File.Exists(sqlFile)) throw new ArgumentException($@"Master sql for client file is not found. Please check : {DB_CLIENT_SQL_FILE}");
            ////if the file exists, then run this file against the adapter gateway but ignore the db name.
            //var content = File.ReadAllText(sqlFile);
            ////We know that the file itself contains "dss_core" as the schema name. Replace that with new one.
            //var exists = await _agw.Scalar(new AdapterArgs(_key) { ExcludeDBInConString = true, Query = GENERAL.SCHEMA_EXISTS }, (NAME, info.DatabaseName));
            //if (exists == null || !exists.IsNumericType() || !double.TryParse(exists.ToString(), out var id) || id < 1) {
            //    content = content.Replace(DB_CLIENT_SEARCH_TERM, info.DatabaseName);
            //    //?? Should we run everything in one go or run as separate statements ???
            //    var result = await _agw.NonQuery(new AdapterArgs(_key) { ExcludeDBInConString = true, Query = content });
            //}
            //exists = await _agw.Scalar(new AdapterArgs(_key) { ExcludeDBInConString = true, Query = GENERAL.SCHEMA_EXISTS }, (NAME, info.DatabaseName));
            //if (exists == null) throw new ArgumentException($@"Unable to generate the database {info.DatabaseName}");
            ////We create an adapter with this Cuid and store them.
            //_agw.DuplicateAdapter(_key, info.Cuid, ("database", info.DatabaseName));
        }
    }
}
