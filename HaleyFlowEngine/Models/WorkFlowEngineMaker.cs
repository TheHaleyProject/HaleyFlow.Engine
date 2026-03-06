using Haley.Abstractions;
using Haley.Enums;
using Haley.Services;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Models {
    public sealed class WorkFlowEngineMaker : DbInstanceMaker {
        const string FALLBACK_DB_NAME = "wf_engine";
        const string EMBEDDED_SQL_RESOURCE = "Haley.Scripts.lc_state.sql";
        const string REPLACE_DBNAME = "lcstate";
        public WorkFlowEngineOptions? Options { get; set; }
        public WorkFlowEngineMaker() {
            FallbackDbName = FALLBACK_DB_NAME;
            ReplaceDbName = REPLACE_DBNAME;
            SqlContent = Encoding.UTF8.GetString(ResourceUtils.GetEmbeddedResource(EMBEDDED_SQL_RESOURCE));
        }
    }
}
