using Haley.Abstractions;
using Haley.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Services {
    public sealed class BluePrintManager : IBlueprintManager {
        private readonly IWorkFlowDAL _dal;

        private readonly Dictionary<long, LifeCycleBlueprint> _byVersion = new();
        private readonly Dictionary<string, LifeCycleBlueprint> _byEnvName = new();

        public BluePrintManager(IWorkFlowDAL dal) => _dal = dal;

        public async Task<DbRow> GetLatestDefVersionAsync(int envCode, string defName, CancellationToken ct = default) {
            var load = new DbExecutionLoad { Ct = ct };
            var row = await _dal.Blueprint.GetLatestDefVersionAsync(envCode, Normalize(defName), load).ConfigureAwait(false);
            return row ?? throw new InvalidOperationException("Definition version not found.");
        }

        public async Task<DbRow> GetDefVersionByIdAsync(long defVersionId, CancellationToken ct = default) {
            var load = new DbExecutionLoad { Ct = ct };
            var row = await _dal.Blueprint.GetDefVersionByIdAsync(defVersionId, load).ConfigureAwait(false);
            return row ?? throw new InvalidOperationException("Definition version not found.");
        }

        public async Task<LifeCycleBlueprint> GetBlueprintLatestAsync(int envCode, string defName, CancellationToken ct = default) {
            var key = $"{envCode}:{Normalize(defName)}";
            if (_byEnvName.TryGetValue(key, out var cached)) return cached;

            var dv = await GetLatestDefVersionAsync(envCode, defName, ct).ConfigureAwait(false);

            var bp = new LifeCycleBlueprint {
                DefinitionVersionId = dv.GetLong("id"),
                DefinitionId = dv.GetLong("parent"),
                DefinitionName = Normalize(defName),
                Version = dv.GetInt("version")
            };

            _byEnvName[key] = bp;
            _byVersion[bp.DefinitionVersionId] = bp;
            return bp;
        }

        public async Task<LifeCycleBlueprint> GetBlueprintByVersionIdAsync(long defVersionId, CancellationToken ct = default) {
            if (_byVersion.TryGetValue(defVersionId, out var cached)) return cached;

            var dv = await GetDefVersionByIdAsync(defVersionId, ct).ConfigureAwait(false);

            var bp = new LifeCycleBlueprint {
                DefinitionVersionId = dv.GetLong("id"),
                DefinitionId = dv.GetLong("parent"),
                Version = dv.GetInt("version"),
                DefinitionName = string.Empty // optional: fetch from definition table later if needed
            };

            _byVersion[defVersionId] = bp;
            return bp;
        }

        public void Clear() {
            _byVersion.Clear();
            _byEnvName.Clear();
        }

        public void Invalidate(int envCode, string defName) {
            _byEnvName.Remove($"{envCode}:{Normalize(defName)}");
        }

        public void Invalidate(long defVersionId) {
            _byVersion.Remove(defVersionId);
        }
        private static string Normalize(string s) => s.Trim().ToLowerInvariant();
    }
}
