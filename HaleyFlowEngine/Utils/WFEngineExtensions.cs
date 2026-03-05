using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Utils {
    public static class WFEngineExtensions {
        const string FALLBACK_DB_NAME = "wf_engine";
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
        public static WorkFlowEngineMaker WithOptions(this WorkFlowEngineMaker input, WorkFlowEngineOptions options) {
            input.Options = options;
            return input;
        }
        public static WorkFlowEngineMaker WithConnectionString(this WorkFlowEngineMaker input,string con_string) {
            input.ConnectionString = con_string;
            return input;
        }
        public static WorkFlowEngineMaker WithAdapterKey(this WorkFlowEngineMaker input, string adapterKey) {
            input.AdapterKey = adapterKey;
            return input;
        }
        #endregion
        public static async Task<IWorkFlowEngine> Build(this WorkFlowEngineMaker input, IAdapterGateway agw) {
          
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
