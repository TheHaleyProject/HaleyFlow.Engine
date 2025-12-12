using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Internal {
    internal class QueryFields {
        public const string ID = "@ID";
        public const string GUID = "@GUID";
        public const string ENV = "@ENV";
        public const string PARENT = "@PARENT";
        public const string DEF_VERSION = "@DEF_VERSION";
        public const string VERSION = "@VERSION";
        public const string DISPLAY_NAME = "@DISPLAY_NAME";
        public const string NAME = "@NAME";
        public const string DESCRIPTION = "@DESCRIPTION";
        public const string CATEGORY = "@CATEGORY";
        public const string DATA = "@DATA";
        public const string CODE = "@CODE";
        public const string FLAGS = "@FLAGS";
        public const string TIMEOUT = "@TIMEOUT";
        public const string TIMEOUT_MODE = "@TIMEOUT_MODE";
        public const string TIMEOUT_EVENT = "@TIMEOUT_EVENT";
        public const string FROM_STATE = "@FROM_STATE";
        public const string TO_STATE = "@TO_STATE";
        public const string EVENT = "@EVENT";
        public const string CURRENT_STATE = "@CURRENT_STATE";
        public const string TRANSITION_LOG = "@TRANSITION_LOG";
        public const string LAST_EVENT = "@LAST_EVENT";
        public const string EXTERNAL_REF = "@EXTERNAL_REF";
        public const string INSTANCE_ID = "@INSTANCE_ID";
        public const string ACTOR = "@ACTOR";
        public const string METADATA = "@METADATA";
        public const string MESSAGE_ID = "@MESSAGE_ID";
        public const string CONSUMER = "@CONSUMER";
        public const string ACK_STATUS = "@ACK_STATUS";
        public const string MAX_RETRY = "@MAX_RETRY";
        public const string RETRY_AFTER_MIN = "@RETRY_AFTER_MIN";
        public const string CREATED = "@CREATED";
        public const string MODIFIED = "@MODIFIED";
        public const string RETENTION_DAYS = "@RETENTION_DAYS";
    }
}
