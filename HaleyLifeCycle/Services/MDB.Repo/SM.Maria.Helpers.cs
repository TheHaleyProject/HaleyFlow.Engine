using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Haley.Utils;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {
        private  (string sql, (string, object)[] args) BuildExists(LifeCycleEntity entity, LifeCycleKey key) {
            switch (entity) {
                case LifeCycleEntity.Environment:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_ENVIRONMENT.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>())}),
                        LifeCycleKeyType.Code => (QRY_ENVIRONMENT.EXISTS_BY_CODE, new (string,object)[] { (CODE, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Name => (QRY_ENVIRONMENT.EXISTS_BY_NAME, new (string,object)[] { (NAME, key.keys[0].As<string>()) }),
                        _ => throw new NotSupportedException($"ENV Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Category:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_CATEGORY.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Name => (QRY_CATEGORY.EXISTS_BY_NAME, new (string,object)[] { (NAME, key.keys[0].As<string>()) }),
                        _ => throw new NotSupportedException($"CATEGORY Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Definition:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_DEFINITION.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Guid => (QRY_DEFINITION.EXISTS_BY_GUID, new (string,object)[] { (GUID, key.keys[0].As<Guid>()) }),
                        LifeCycleKeyType.Composite => BuildDefinitionExistsComposite(key),
                        _ => throw new NotSupportedException($"DEFINITION Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.DefinitionVersion:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_DEF_VERSION.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Guid => (QRY_DEF_VERSION.EXISTS_BY_GUID, new (string,object)[] { (GUID, key.keys[0].As<Guid>()) }),
                        LifeCycleKeyType.Composite => (QRY_DEF_VERSION.EXISTS_BY_PARENT_AND_VERSION, new (string,object)[] { (PARENT, key.keys[0].As<int>()), (VERSION, key.keys[1].As<int>()) }),
                        _ => throw new NotSupportedException($"DEF_VERSION Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.State:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_STATE.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Composite => (QRY_STATE.EXISTS_BY_VERSION_AND_NAME, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (NAME, key.keys[1].As<string>()) }),
                        _ => throw new NotSupportedException($"STATE Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Event:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_EVENT.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Composite => BuildEventExistsComposite(key),
                        _ => throw new NotSupportedException($"EVENT Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Transition:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_TRANSITION.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Composite => BuildTransitionExistsComposite(key),
                        _ => throw new NotSupportedException($"TRANSITION Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Instance:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_INSTANCE.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<long>()) }),
                        LifeCycleKeyType.Guid => (QRY_INSTANCE.EXISTS_BY_GUID, new (string,object)[] { (GUID, key.keys[0].As<Guid>()) }),
                        LifeCycleKeyType.Composite => (QRY_INSTANCE.EXISTS_BY_VERSION_AND_REF, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (EXTERNAL_REF, key.keys[1].As<string>()) }),
                        _ => throw new NotSupportedException($"INSTANCE Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.TransitionLog:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_TRANSITION_LOG.EXISTS_LOG_BY_ID, new (string,object)[] { (ID, key.keys[0].As<long>()) }),
                        _ => throw new NotSupportedException($"TRANSITION_LOG Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.TransitionData:
                    return key.Type switch {
                        LifeCycleKeyType.Composite => (QRY_TRANSITION_DATA.EXISTS_BY_TRANSITION_LOG, new (string,object)[] { (TRANSITION_LOG, key.keys[0].As<long>()) }),
                        LifeCycleKeyType.Id => (QRY_TRANSITION_DATA.EXISTS_BY_TRANSITION_LOG, new (string,object)[] { (TRANSITION_LOG, key.keys[0].As<long>()) }),
                        _ => throw new NotSupportedException($"TRANSITION_DATA Exists unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.AckLog:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_ACK_LOG.EXISTS_BY_ID, new (string,object)[] { (ID, key.keys[0].As<long>()) }),
                        LifeCycleKeyType.Name => (QRY_ACK_LOG.EXISTS_BY_MESSAGE_ID, new (string,object)[] { (MESSAGE_ID, key.keys[0].As<string>()) }),
                        LifeCycleKeyType.Composite => (QRY_ACK_LOG.EXISTS_BY_CONSUMER_AND_TRANSITION_LOG, new (string,object)[] { (TRANSITION_LOG, key.keys[0].As<long>()), (CONSUMER, key.keys[1].As<int>()) }),
                        _ => throw new NotSupportedException($"ACK_LOG Exists unsupported key: {key.Type}")
                    };

                default:
                    throw new NotSupportedException($"Exists unsupported entity: {entity}");
            }
        }

        private  (string sql, (string, object)[] args) BuildDelete(LifeCycleEntity entity, LifeCycleKey key) {
            switch (entity) {
                case LifeCycleEntity.Environment:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_ENVIRONMENT.DELETE_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Code => (QRY_ENVIRONMENT.DELETE_BY_CODE, new (string,object)[] { (CODE, key.keys[0].As<int>()) }),
                        _ => throw new NotSupportedException($"ENV Delete unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Category:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_CATEGORY.DELETE_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        _ => throw new NotSupportedException($"CATEGORY Delete unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Definition:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_DEFINITION.DELETE, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        _ => throw new NotSupportedException($"DEFINITION Delete unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.DefinitionVersion:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_DEF_VERSION.DELETE, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        _ => throw new NotSupportedException($"DEF_VERSION Delete unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.State:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_STATE.DELETE, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        _ => throw new NotSupportedException($"STATE Delete unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Event:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_EVENT.DELETE, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        _ => throw new NotSupportedException($"EVENT Delete unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Transition:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_TRANSITION.DELETE, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        _ => throw new NotSupportedException($"TRANSITION Delete unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Instance:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_INSTANCE.DELETE, new (string,object)[] { (ID, key.keys[0].As<long>()) }),
                        LifeCycleKeyType.Guid => (QRY_INSTANCE.DELETE_BY_GUID, new (string,object)[] { (GUID, key.keys[0].As<Guid>()) }),
                        _ => throw new NotSupportedException($"INSTANCE Delete unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.TransitionData:
                    return (QRY_TRANSITION_DATA.DELETE_BY_LOG, new (string,object)[] { (TRANSITION_LOG, key.keys[0].As<long>()) });

                default:
                    throw new NotSupportedException($"Delete unsupported entity: {entity}");
            }
        }

        private  (string sql, (string, object)[] args) BuildGet(LifeCycleEntity entity, LifeCycleKey key) {
            switch (entity) {
                case LifeCycleEntity.Environment:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_ENVIRONMENT.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Code => (QRY_ENVIRONMENT.GET_BY_CODE, new (string,object)[] { (CODE, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Name => (QRY_ENVIRONMENT.GET_BY_NAME, new (string,object)[] { (NAME, key.keys[0].As<string>()) }),
                        _ => throw new NotSupportedException($"ENV Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Category:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_CATEGORY.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Name => (QRY_CATEGORY.GET_BY_NAME, new (string,object)[] { (NAME, key.keys[0].As<string>()) }),
                        _ => throw new NotSupportedException($"CATEGORY Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Definition:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_DEFINITION.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Guid => (QRY_DEFINITION.GET_BY_GUID, new (string,object)[] { (GUID, key.keys[0].As<Guid>()) }),
                        LifeCycleKeyType.Composite => (QRY_DEFINITION.GET_BY_NAME, new (string,object)[] { (ENV, key.keys[0].As<int>()), (NAME, key.keys[1].As<string>()) }), // env_id + def_name
                        _ => throw new NotSupportedException($"DEFINITION Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.DefinitionVersion:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_DEF_VERSION.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Guid => (QRY_DEF_VERSION.GET_BY_GUID, new (string,object)[] { (GUID, key.keys[0].As<Guid>()) }),
                        _ => throw new NotSupportedException($"DEF_VERSION Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.State:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_STATE.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Composite => (QRY_STATE.GET_BY_NAME, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (NAME, key.keys[1].As<string>()) }),
                        _ => throw new NotSupportedException($"STATE Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Event:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_EVENT.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Composite => BuildEventGetComposite(key),
                        _ => throw new NotSupportedException($"EVENT Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Transition:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_TRANSITION.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<int>()) }),
                        LifeCycleKeyType.Composite => BuildTransitionGetComposite(key),
                        _ => throw new NotSupportedException($"TRANSITION Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.Instance:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_INSTANCE.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<long>()) }),
                        LifeCycleKeyType.Guid => (QRY_INSTANCE.GET_BY_GUID, new (string,object)[] { (GUID, key.keys[0].As<Guid>()) }),
                        LifeCycleKeyType.Parent => BuildParentInstanceQuery(key),
                        LifeCycleKeyType.Composite => (QRY_INSTANCE.GET_BY_REF, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (EXTERNAL_REF, key.keys[1].As<string>()) }),
                        _ => throw new NotSupportedException($"INSTANCE Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.TransitionLog:
                    return key.Type switch {
                        LifeCycleKeyType.Id => (QRY_TRANSITION_LOG.GET_BY_ID, new (string,object)[] { (ID, key.keys[0].As<long>()) }),
                        _ => throw new NotSupportedException($"TRANSITION_LOG Get unsupported key: {key.Type}")
                    };

                case LifeCycleEntity.TransitionData:
                    return (QRY_TRANSITION_DATA.GET_BY_LOG, new (string,object)[] { (TRANSITION_LOG, key.keys[0].As<long>()) });

                default:
                    throw new NotSupportedException($"Get unsupported entity: {entity}");
            }
        }

        private (string sql, (string, object)[] args) BuildParentInstanceQuery(LifeCycleKey key) {
            var instKeys = key.ParseInstanceKey(_agw, _key);
            return (QRY_INSTANCE.GET_BY_REF, new (string, object)[] { (DEF_VERSION, instKeys.definitionVersion), (EXTERNAL_REF, instKeys.externalRef) });
        }

        private  (string sql, (string, object)[] args) BuildList(LifeCycleEntity entity, LifeCycleKey? key) {
            switch (entity) {
                case LifeCycleEntity.Environment:
                    return (QRY_ENVIRONMENT.GET_ALL, Array.Empty<(string, object)>());

                case LifeCycleEntity.Category:
                    return (QRY_CATEGORY.GET_ALL, Array.Empty<(string, object)>());

                case LifeCycleEntity.Definition:
                    return (QRY_DEFINITION.GET_ALL, Array.Empty<(string, object)>());

                case LifeCycleEntity.DefinitionVersion:
                    if (key == null) throw new ArgumentNullException(nameof(key), "DEF_VERSION List needs scope=LifeCycleKey(Id,parentDefinitionId).");
                    return key.Value.Type switch {
                        LifeCycleKeyType.Id => (QRY_DEF_VERSION.GET_BY_PARENT, new (string,object)[] { (PARENT, key.Value.keys[0].As<int>()) }),
                        _ => throw new NotSupportedException($"DEF_VERSION List unsupported scope key: {key.Value.Type}")
                    };

                case LifeCycleEntity.State:
                    if (key == null) throw new ArgumentNullException(nameof(key), "STATE List needs scope=LifeCycleKey(Id,defVersion).");
                    return (QRY_STATE.GET_BY_VERSION_WITH_CATEGORY, new (string,object)[] { (DEF_VERSION, key.Value.keys[0].As<int>()) });

                case LifeCycleEntity.Event:
                    if (key == null) throw new ArgumentNullException(nameof(key), "EVENT List needs scope=LifeCycleKey(Id,defVersion).");
                    return (QRY_EVENT.GET_BY_VERSION, new (string,object)[] { (DEF_VERSION, key.Value.keys[0].As<int>()) });

                case LifeCycleEntity.Transition:
                    if (key == null) throw new ArgumentNullException(nameof(key), "TRANSITION List needs scope=LifeCycleKey(Id,defVersion) OR scope=LifeCycleKey(Composite,defVersion,fromState).");
                    return key.Value.Type switch {
                        LifeCycleKeyType.Id => (QRY_TRANSITION.GET_BY_VERSION, new (string,object)[] { (DEF_VERSION, key.Value.keys[0].As<int>()) }),
                        LifeCycleKeyType.Composite => (QRY_TRANSITION.GET_OUTGOING, new (string,object)[] { (DEF_VERSION, key.Value.keys[0].As<int>()), (FROM_STATE, key.Value.keys[1].As<int>()) }),
                        _ => throw new NotSupportedException($"TRANSITION List unsupported scope key: {key.Value.Type}")
                    };

                case LifeCycleEntity.Instance:
                    if (key == null) throw new ArgumentNullException(nameof(key), "INSTANCE List needs scope=Name(externalRef) or Composite(defVersion, stateId|flags).");
                    return key.Value.Type switch {
                        LifeCycleKeyType.Name => (QRY_INSTANCE.GET_BY_REF_ANY_VERSION, new (string,object)[] { (EXTERNAL_REF, key.Value.keys[0].As<string>()) }),
                        LifeCycleKeyType.Composite => BuildInstanceListComposite(key.Value),
                        _ => throw new NotSupportedException($"INSTANCE List unsupported scope key: {key.Value.Type}")
                    };

                case LifeCycleEntity.TransitionLog:
                    if (key == null) throw new ArgumentNullException(nameof(key), "TRANSITION_LOG List needs scope=Id(instanceId) or Composite(from,to) or Composite(createdFrom,createdTo).");
                    return key.Value.Type switch {
                        LifeCycleKeyType.Id => (QRY_TRANSITION_LOG.GET_BY_INSTANCE, new (string,object)[] { (INSTANCE_ID, key.Value.keys[0].As<long>()) }),
                        LifeCycleKeyType.Composite => BuildTransitionLogListComposite(key.Value),
                        _ => throw new NotSupportedException($"TRANSITION_LOG List unsupported scope key: {key.Value.Type}")
                    };

                default:
                    throw new NotSupportedException($"List unsupported entity: {entity}");
            }
        }

        // composite helpers
        private  (string sql, (string, object)[] args) BuildDefinitionExistsComposite(LifeCycleKey key) {
            // Supported:
            //  1) (env_code:int, def_name:string)  -> EXISTS_BY_ENV_CODE_AND_NAME
            //  2) (env_id:long, def_name:string)   -> EXISTS_BY_ENV_AND_NAME
            if (key.keys[1] is null) throw new ArgumentException("Definition Exists composite needs (A,B).");
            if (key.keys[0] is long) return (QRY_DEFINITION.EXISTS_BY_ENV_AND_NAME, new (string,object)[] { (ENV, key.keys[0].As<int>()), (NAME, key.keys[1].As<string>()) });
            return (QRY_DEFINITION.EXISTS_BY_ENV_CODE_AND_NAME, new (string,object)[] { (CODE, key.keys[0].As<int>()), (NAME, key.keys[1].As<string>()) });
        }

        private  (string sql, (string, object)[] args) BuildEventExistsComposite(LifeCycleKey key) {
            // Supported:
            //  (defVersion:int, code:int) OR (defVersion:int, name:string)
            if (key.keys[1] is null) throw new ArgumentException("Event Exists composite needs (defVersion, code|name).");
            return key.keys[1] switch {
                int => (QRY_EVENT.EXISTS_BY_VERSION_AND_CODE, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (CODE, key.keys[1].As<int>()) }),
                string => (QRY_EVENT.EXISTS_BY_VERSION_AND_NAME, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (NAME, key.keys[1].As<string>()) }),
                _ => throw new NotSupportedException("Event Exists composite supports B as int(code) or string(name).")
            };
        }

        private  (string sql, (string, object)[] args) BuildEventGetComposite(LifeCycleKey key) {
            // Supported:
            //  (defVersion:int, code:int) OR (defVersion:int, name:string)
            if (key.keys[1] is null) throw new ArgumentException("Event Get composite needs (defVersion, code|name).");
            return key.keys[1] switch {
                int => (QRY_EVENT.GET_BY_CODE, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (CODE, key.keys[1].As<int>()) }),
                string => (QRY_EVENT.GET_BY_NAME, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (NAME, key.keys[1].As<string>()) }),
                _ => throw new NotSupportedException("Event Get composite supports B as int(code) or string(name).")
            };
        }

        private  (string sql, (string, object)[] args) BuildTransitionExistsComposite(LifeCycleKey key) {
            // Supported:
            //  A = object[] { defVersion, fromState, toState, eventId }
            if (key.keys[0] is object[] a && a.Length >= 4)
                return (QRY_TRANSITION.EXISTS_BY_UNQ, new (string,object)[] { (DEF_VERSION, a[0].As<int>()), (FROM_STATE, a[1].As<int>()), (TO_STATE, a[2].As<int>()), (EVENT, a[3].As<int>()) });

            // Fallback: (defVersion, fromState) + (eventId in B) is NOT unique -> not supported for Exists.
            throw new NotSupportedException("Transition Exists composite requires A=object[]{defVersion,fromState,toState,eventId}.");
        }

        private  (string sql, (string, object)[] args) BuildTransitionGetComposite(LifeCycleKey key) {
            // Supported:
            //  A = object[] { defVersion, fromState, eventId } -> GET_TRANSITION
            if (key.keys[0] is object[] a && a.Length >= 3)
                return (QRY_TRANSITION.GET_TRANSITION, new (string,object)[] { (DEF_VERSION, a[0].As<int>()), (FROM_STATE, a[1].As<int>()), (EVENT, a[2].As<int>()) });

            // Supported:
            //  (defVersion:int, fromState:int) -> GET_OUTGOING (single row not guaranteed, but caller asked Get)
            if (key.keys[1] is not null)
                return (QRY_TRANSITION.GET_OUTGOING, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (FROM_STATE, key.keys[1].As<int>()) });

            throw new NotSupportedException("Transition Get composite requires A=object[]{defVersion,fromState,eventId} OR (A=defVersion,B=fromState).");
        }

        private  (string sql, (string, object)[] args) BuildInstanceListComposite(LifeCycleKey key) {
            // Supported:
            //  (defVersion:int, stateId:int) -> GET_BY_STATE_IN_VERSION
            //  (defVersion:int, flags:int|enum) -> GET_BY_FLAGS_IN_VERSION
            if (key.keys[1] is null) throw new ArgumentException("Instance List composite needs (defVersion, stateId|flags).");

            if (key.keys[1] is int bInt) {
                return (QRY_INSTANCE.GET_BY_STATE_IN_VERSION, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (CURRENT_STATE, bInt) });
            }

            // flags (int/enum)
            return (QRY_INSTANCE.GET_BY_FLAGS_IN_VERSION, new (string,object)[] { (DEF_VERSION, key.keys[0].As<int>()), (FLAGS, key.keys[1].As<int>()) });
        }

        private  (string sql, (string, object)[] args) BuildTransitionLogListComposite(LifeCycleKey key) {
            // Supported:
            //  A = object[] { fromState:int, toState:int } -> GET_BY_STATE_CHANGE
            //  A = object[] { createdFrom:DateTime, createdTo:DateTime } -> GET_BY_DATE_RANGE
            if (key.keys[0] is object[] a && a.Length >= 2) {
                if (a[0] is DateTime || a[1] is DateTime) {
                    return (QRY_TRANSITION_LOG.GET_BY_DATE_RANGE, new (string,object)[] { (CREATED, a[0]), (MODIFIED, a[1]) });
                }
                return (QRY_TRANSITION_LOG.GET_BY_STATE_CHANGE, new (string,object)[] { (FROM_STATE, a[0].As<int>()), (TO_STATE, a[1].As<int>()) });
            }

            throw new NotSupportedException("TransitionLog List composite requires A=object[]{from,to} OR A=object[]{createdFrom,createdTo}.");
        }

        // pagination helper
        private  string ApplyPaginationIfMissing(string sql) {
            if (string.IsNullOrWhiteSpace(sql)) return sql;
            var s = sql.Trim();
            if (s.Contains(SKIP, StringComparison.Ordinal) || s.Contains(LIMIT, StringComparison.Ordinal)) return s; // already parameterized
            if (s.IndexOf("LIMIT", StringComparison.OrdinalIgnoreCase) >= 0) return s; // already has limit
            if (s.EndsWith(";", StringComparison.Ordinal)) s = s[..^1];
            return $"{s} LIMIT {SKIP}, {LIMIT};";
        }

        private  bool HasArg(List<(string, object)> args, string name) {
            for (int i = 0; i < args.Count; i++) if (args[i].Item1 == name) return true;
            return false;
        }
    }
}
