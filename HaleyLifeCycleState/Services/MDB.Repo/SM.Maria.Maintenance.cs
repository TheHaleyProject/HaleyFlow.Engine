using Haley.Abstractions;
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
        // 🔹 MAINTENANCE / UTILITIES
        // ----------------------------------------------------------

        public async Task<IFeedback<int>> PurgeOldLogs(int daysToKeep) {
            var fb = new Feedback<int>();
            try {
                var result = await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_MAINTENANCE.PURGE_OLD_LOGS },
                    (FLAGS, daysToKeep));
                if (result == null || result is not int deletedCount)
                    return fb.SetMessage("PurgeOldLogs operation did not return a valid deleted count.");
                return fb.SetStatus(true).SetResult(deletedCount);
            } catch (Exception ex) {
                _logger?.LogError(ex, "PurgeOldLogs failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback<int>> CountInstances(int defVersion, int flagsFilter = 0) {
            var fb = new Feedback<int>();
            try {
                var result = await _agw.Scalar(new AdapterArgs(_key) { Query = QRY_MAINTENANCE.COUNT_INSTANCES },
                    (DEF_VERSION, defVersion),
                    (FLAGS, flagsFilter));

                if (result == null || !int.TryParse(result.ToString(), out var count))
                    return fb.SetMessage("Unable to count instances.");

                return fb.SetStatus(true).SetResult(count);
            } catch (Exception ex) {
                _logger?.LogError(ex, "CountInstances failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }

        public async Task<IFeedback> RebuildIndexes() {
            var fb = new Feedback();
            try {
                await _agw.NonQuery(new AdapterArgs(_key) { Query = QRY_MAINTENANCE.REBUILD_INDEXES });
                return fb.SetStatus(true).SetMessage("Database indexes optimized successfully.");
            } catch (Exception ex) {
                _logger?.LogError(ex, "RebuildIndexes failed.");
                if (ThrowExceptions) throw;
                return fb.SetMessage(ex.Message);
            }
        }
    }
}
