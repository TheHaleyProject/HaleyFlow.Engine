using Haley.Enums;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Utils {
    public static class LifeCycleKeys {
        public static LifeCycleKey Instance(long id) => new(LifeCycleKeyType.Id, id);
        public static LifeCycleKey Instance(int defVersion, string externalRef) => new(LifeCycleKeyType.Composite, defVersion, externalRef);
        public static LifeCycleKey Instance(string definition, string externalRef, int environmentCode = 0) => new(LifeCycleKeyType.Parent, definition, externalRef,environmentCode);

        public static LifeCycleKey Ack(string messageId) => new(LifeCycleKeyType.MessageId, messageId);
        public static LifeCycleKey Ack(long transitionLogId, int consumer) => new(LifeCycleKeyType.Composite, transitionLogId, consumer);

        public static LifeCycleKey Definition(long id) => new(LifeCycleKeyType.Id, id);
        public static LifeCycleKey Definition(int envCode, string defName) => new(LifeCycleKeyType.Composite, envCode, defName);

        public static LifeCycleKey Event(int defVersion, int code) => new(LifeCycleKeyType.Composite, defVersion, code);
        public static LifeCycleKey Event(int defVersion, string name) => new(LifeCycleKeyType.Composite, defVersion, name);
    }
}
