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
        }
    }
}
