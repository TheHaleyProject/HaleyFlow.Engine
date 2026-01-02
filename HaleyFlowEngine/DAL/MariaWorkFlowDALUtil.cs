using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;

namespace Haley.Utils {
    public sealed class MariaWorkFlowDALUtil : IWorkFlowDALUtil {
        private readonly IAdapterGateway _agw;
        private readonly string _key;

        public MariaWorkFlowDALUtil(IAdapterGateway agw, string key) {
            _agw = agw ?? throw new ArgumentNullException(nameof(agw));
            _key = key ?? throw new ArgumentNullException(nameof(key));
        }

        public async Task<int> ExecAsync(string sql, DbExecutionLoad load = default, params DbArg[] args) {
            load.Ct.ThrowIfCancellationRequested();
            var fb = await _agw.NonQueryAsync(new AdapterArgs(_key) { Query = sql}.ForTransaction(load.Handler,false), ToAgwArgs(args));
            if (!fb.Status) throw new InvalidOperationException(fb.Message ?? "NonQuery failed.");
            return fb.Result;
        }
        public async Task<T?> ScalarAsync<T>(string sql, DbExecutionLoad load = default, params DbArg[] args) {
            load.Ct.ThrowIfCancellationRequested();
            var fb = await _agw.ScalarAsync<T>(new AdapterArgs(_key) { Query = sql }.ForTransaction(load.Handler, false), ToAgwArgs(args));
            if (!fb.Status) throw new InvalidOperationException(fb.Message ?? "Scalar failed.");
            return fb.Result;
        }
        public async Task<DbRow?> RowAsync(string sql, DbExecutionLoad load = default, params DbArg[] args) {
            load.Ct.ThrowIfCancellationRequested();
            var fb = await _agw.ReadSingleAsync(new AdapterArgs(_key) { Query = sql }.ForTransaction(load.Handler, false), ToAgwArgs(args));
            if (!fb.Status) throw new InvalidOperationException(fb.Message ?? "ReadSingle failed.");
            return fb.Result;
        }
        public async Task<DbRows> RowsAsync(string sql, DbExecutionLoad load = default, params DbArg[] args) {
            load.Ct.ThrowIfCancellationRequested();
            var fb = await _agw.ReadAsync(new AdapterArgs(_key) { Query = sql }.ForTransaction(load.Handler, false), ToAgwArgs(args));
            if (!fb.Status) throw new InvalidOperationException(fb.Message ?? "Read failed.");
            return fb.Result;
        }

        private static (string key, object value)[] ToAgwArgs(DbArg[]? args) {
            if (args == null || args.Length == 0) return Array.Empty<(string key, object value)>();

            var arr = new (string key, object value)[args.Length];
            for (int i = 0; i < args.Length; i++) {
                // AGW expects object (non-nullable) — convert null to DBNull.Value (safe for DB)
                var v = args[i].Value ?? DBNull.Value;
                arr[i] = (args[i].Name, v);
            }
            return arr;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
