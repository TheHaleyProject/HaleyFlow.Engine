using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Internal {
    internal class QueryFields {
        public const string ID = "@ID";
        public const string PARENT_ID = "@PARENT_ID";

        public const string NAME = "@NAME";
        public const string CODE = "@CODE";
        public const string GUID = "@GUID";
        public const string HASH = "@HASH";
        public const string CATEGORY_ID = "@CATEGORY_ID";
        public const string TIMEOUT_MODE = "@TIMEOUT_MODE";
        public const string TIMEOUT_MINUTES = "@TIMEOUT_MINUTES";
        public const string TIMEOUT_EVENT = "@TIMEOUT_EVENT";

        public const string DISPLAY_NAME = "@DISPLAY_NAME";
        public const string DESCRIPTION = "@DESCRIPTION";
        public const string CONTENT = "@CONTENT";
        public const string DATA = "@DATA";
        public const string PAYLOAD = "@PAYLOAD";
        public const string VERSION = "@VERSION";
        public const string DEF_NAME = "@DEF_NAME";
        public const string ENV_NAME = "@ENV_NAME";
        public const string FROZEN = "@FROZEN";

        public const string INSTANCE_ID = "@INSTANCE_ID";
        public const string EXTERNAL_REF = "@EXTERNAL_REF";
        public const string POLICY_ID = "@POLICY_ID";
        public const string CONSUMER = "@CONSUMER";
        public const string MAX_RETRY = "@MAX_RETRY";
        public const string RECHECK_SECONDS = "@RECHECK_SECONDS";

        public const string STATE_ID = "@STATE_ID";
        public const string FROM_ID = "@FROM_ID";
        public const string TO_ID = "@TO_ID";
        public const string EVENT_ID = "@EVENT_ID";

        public const string FLAGS = "@FLAGS";

        public const string ACTOR = "@ACTOR";
        public const string ACTOR_ID = "@ACTOR_ID";

        public const string ON_ENTRY = "@ON_ENTRY";
        public const string ROUTE = "@ROUTE";

        public const string ACK_ID = "@ACK_ID";
        public const string LC_ID = "@LC_ID";
        public const string ENV_ID = "@ENV_ID";
        public const string HOOK_ID = "@HOOK_ID";
        public const string TTL_SECONDS = "@TTL_SECONDS";
        public const string CONSUMER_ID = "@CONSUMER_ID";
        public const string CONSUMER_GUID = "@CONSUMER_GUID";
        public const string SOURCE_ID = "@SOURCE_ID";
        public const string ACK_STATUS = "@ACK_STATUS";
        public const string OLDER_THAN = "@OLDER_THAN";
        public const string NEXT_DUE = "@NEXT_DUE";
        public const string MESSAGE = "@MESSAGE";

        public const string ACTIVITY_ID = "@ACTIVITY_ID";
        public const string STATUS_ID = "@STATUS_ID";

        public const string TAKE = "@TAKE";
        public const string SKIP = "@SKIP";
    }
}
