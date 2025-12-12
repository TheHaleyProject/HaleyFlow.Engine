using Haley.Abstractions;
using Haley.Internal;
using Haley.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB {
        public Task<IFeedback<Dictionary<string, object>>> RegisterEvent(string displayName, int code, int defVersion) => _agw.ReadSingleAsync(_key, QRY_EVENT.INSERT, (DISPLAY_NAME, displayName), (CODE, code), (DEF_VERSION, defVersion));
        public Task<IFeedback<List<Dictionary<string, object>>>> GetEventsByVersion(int defVersion) => _agw.ReadAsync(_key, QRY_EVENT.GET_BY_VERSION, (DEF_VERSION, defVersion));
        public Task<IFeedback<Dictionary<string, object>>> GetEventByCode(int defVersion, int code) => _agw.ReadSingleAsync(_key, QRY_EVENT.GET_BY_CODE, (DEF_VERSION, defVersion), (CODE, code));
        public Task<IFeedback<Dictionary<string, object>>> GetEventByName(int defVersion, string name) => _agw.ReadSingleAsync(_key, QRY_EVENT.GET_BY_NAME, (DEF_VERSION, defVersion), (NAME, name));
        public Task<IFeedback<bool>> DeleteEvent(int eventId) => _agw.NonQueryAsync(_key, QRY_EVENT.DELETE, (ID, eventId));

    }
}
