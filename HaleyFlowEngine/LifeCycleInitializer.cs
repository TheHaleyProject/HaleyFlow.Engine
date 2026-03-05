using Azure;
using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Utils {
    public static class LifeCycleInitializer {
        const string FALLBACK_DB_NAME = "hdb_lc_state";
        const string EMBEDDED_SQL_RESOURCE = "Haley.Scripts.lc_state.sql";
        const string REPLACE_DBNAME = "lcstate";
        static async Task<IFeedback<string>> InitializeAsyncWithConString(IAdapterGateway agw,  string connectionstring) {
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

        static Task<IFeedback> InitializeAsync(IAdapterGateway agw, string adapterKey) {
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

        #region Wrapper making 
        public static LCInitializerWrapper WithOptions(WorkFlowEngineOptions options) {
            return new LCInitializerWrapper() { Options = options };
        }
        public static LCInitializerWrapper WithOptions(this LCInitializerWrapper input, WorkFlowEngineOptions options) {
            input.Options = options;
            return input;
        }
        public static LCInitializerWrapper WithConnectionString(string con_string) {
            return new LCInitializerWrapper() { ConnectionString = con_string };
        }
        public static LCInitializerWrapper WithConnectionString(this LCInitializerWrapper input,string con_string) {
            input.ConnectionString = con_string;
            return input;
        }
        public static LCInitializerWrapper WithAdapterKey(string adapterKey) {
            return new LCInitializerWrapper() { AdapterKey = adapterKey };
        }
        public static LCInitializerWrapper WithAdapterKey(this LCInitializerWrapper input, string adapterKey) {
            input.AdapterKey = adapterKey;
            return input;
        }
        #endregion
        public static async Task<IWorkFlowEngine> Build(this LCInitializerWrapper input, IAdapterGateway agw) {
          
            if (input == null) throw new ArgumentException(nameof(input));
            bool isInitialized = false;
            string adapterKey = string.Empty;
            string errMessage = string.Empty;

            //DB Initialization
            do {
                //Try initialization with Connection string
                if (!string.IsNullOrWhiteSpace(input.ConnectionString)) {
                    var conResponse = await InitializeAsyncWithConString(agw, input.ConnectionString);
                    if (conResponse != null && conResponse.Status && conResponse.Result != null) {
                        adapterKey = conResponse.Result;
                    } else {
                        errMessage = conResponse?.Message;
                    }
                }
                if (!string.IsNullOrWhiteSpace(adapterKey)) break; //We hvae a key, go ahead.
                if (string.IsNullOrWhiteSpace(input.AdapterKey)) break; //We dont have a key but also, dont have adapterkey from input.

                //Try with Adapter key
                var fb = await InitializeAsync(agw, input.AdapterKey);
                if (fb != null && fb.Status) {
                    adapterKey = input.AdapterKey;
                } else {
                    errMessage = fb?.Message;
                }

            } while (false);

            if (string.IsNullOrWhiteSpace(adapterKey)) throw new ArgumentException($@"Unable to initialize the database for the lifecycle state machine. {errMessage}");
            var dal = new MariaWorkFlowDAL(agw, adapterKey);
            return new WorkFlowEngine(dal, input.Options);
        }
    }
}
