using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {
        public Task<IFeedback<Dictionary<string, object>>> RegisterTransition(int fromState, int toState, int eventId, int defVersion) => _agw.ReadSingleAsync(_key, QRY_TRANSITION.INSERT, (FROM_STATE, fromState), (TO_STATE, toState), (EVENT, eventId), (DEF_VERSION, defVersion));
        public Task<IFeedback<List<Dictionary<string, object>>>> GetTransitionsByVersion(int defVersion) => _agw.ReadAsync(_key, QRY_TRANSITION.GET_BY_VERSION, (DEF_VERSION, defVersion));
        public Task<IFeedback<Dictionary<string, object>>> GetTransition(int fromState, int eventId, int defVersion) => _agw.ReadSingleAsync(_key, QRY_TRANSITION.GET_TRANSITION, (FROM_STATE, fromState), (EVENT, eventId), (DEF_VERSION, defVersion));
        public Task<IFeedback<List<Dictionary<string, object>>>> GetOutgoingTransitions(int fromState, int defVersion) => _agw.ReadAsync(_key, QRY_TRANSITION.GET_OUTGOING, (FROM_STATE, fromState), (DEF_VERSION, defVersion));
        public Task<IFeedback<bool>> DeleteTransition(int transitionId) => _agw.NonQueryAsync(_key, QRY_TRANSITION.DELETE, (ID, transitionId));
    }
}
