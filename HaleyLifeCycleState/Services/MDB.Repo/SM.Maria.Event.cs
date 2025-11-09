using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class StateMachineMariaRepo {

        // ----------------------------------------------------------
        // EVENT MANAGEMENT
        // ----------------------------------------------------------

        public async Task<IFeedback<long>> RegisterEvent(string displayName, int defVersion) {
            var fb = new Feedback<long>();
            try {
                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_EVENT.INSERT },
                    (DISPLAY_NAME, displayName),
                    (DEF_VERSION, defVersion));

                if (result == null || !long.TryParse(result.ToString(), out var id))
                    return fb.SetMessage($"Unable to register event: {displayName}");

                return fb.SetStatus(true).SetResult(id);
            } catch (Exception ex) {
                _logger?.LogError(ex, "RegisterEvent failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<List<Dictionary<string, object>>>> GetEventsByVersion(int defVersion) {
            var fb = new Feedback<List<Dictionary<string, object>>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_EVENT.GET_BY_VERSION },
                    (DEF_VERSION, defVersion));

                if (result is not List<Dictionary<string, object>> list || list.Count == 0)
                    return fb.SetMessage($"No events found for version {defVersion}.");

                return fb.SetStatus(true).SetResult(list);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetEventsByVersion failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<Dictionary<string, object>>> GetEventByName(int defVersion, string name) {
            var fb = new Feedback<Dictionary<string, object>>();
            try {
                var result = await _agw.Read(new AdapterArgs(_key) { Query = QRY_EVENT.GET_BY_NAME, Filter = ResultFilter.FirstDictionary },
                    (DEF_VERSION, defVersion),
                    (NAME, name.ToLower()));

                if (result is not Dictionary<string, object> dic)
                    return fb.SetMessage($"Event '{name}' not found for version {defVersion}.");

                return fb.SetStatus(true).SetResult(dic);
            } catch (Exception ex) {
                _logger?.LogError(ex, "GetEventByName failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<bool>> DeleteEvent(int eventId) {
            var fb = new Feedback<bool>();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_EVENT.DELETE }, (ID, eventId));
                return fb.SetStatus(true).SetResult(true);
            } catch (Exception ex) {
                _logger?.LogError(ex, "DeleteEvent failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
