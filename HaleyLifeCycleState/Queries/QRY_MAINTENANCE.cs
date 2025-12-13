using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Internal {
    internal class QRY_MAINTENANCE {
        public const string PURGE_OLD_LOGS = $@"DELETE a FROM ack_log a JOIN transition_log tl ON tl.id = a.transition_log WHERE tl.created < DATE_SUB(current_timestamp(), INTERVAL {RETENTION_DAYS} DAY); DELETE td FROM transition_data td JOIN transition_log tl ON tl.id = td.transition_log WHERE tl.created < DATE_SUB(current_timestamp(), INTERVAL {RETENTION_DAYS} DAY); DELETE FROM transition_log WHERE created < DATE_SUB(current_timestamp(), INTERVAL {RETENTION_DAYS} DAY);";
        public const string COUNT_INSTANCES = $@"SELECT COUNT(*) AS total FROM instance WHERE def_version = {DEF_VERSION} AND ((((flags & {FLAGS}) = {FLAGS})) OR {FLAGS} = 0);";
        public const string REBUILD_INDEXES = $@"OPTIMIZE TABLE definition, def_version, state, events, transition, instance, transition_log, transition_data, ack_log, category;";
    }

}
