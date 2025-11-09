using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_MAINTENANCE {
        public const string PURGE_OLD_LOGS = $@"DELETE FROM transition_log WHERE created < DATE_SUB(NOW(), INTERVAL {FLAGS} DAY);";
        public const string COUNT_INSTANCES = $@"SELECT COUNT(*) AS total FROM instance WHERE def_version = {DEF_VERSION} AND ((flags & {FLAGS}) = {FLAGS} OR {FLAGS} = 0);";
        public const string REBUILD_INDEXES = $@"OPTIMIZE TABLE definition, def_version, state, events, transition, instance, transition_log;";
    }

}
