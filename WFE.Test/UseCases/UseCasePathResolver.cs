using System;
using System.IO;

namespace WFE.Test.UseCases {
    internal static class UseCasePathResolver {
        public static string Resolve(params string[] relativeSegments) {
            if (relativeSegments == null || relativeSegments.Length == 0) {
                throw new ArgumentException("Use-case path is required.", nameof(relativeSegments));
            }

            var relativePath = Path.Combine(relativeSegments);

            var p1 = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(p1)) return p1;

            var p2 = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
            if (File.Exists(p2)) return p2;

            var probe = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (probe != null) {
                var marker = Path.Combine(probe.FullName, "WFE.Test.csproj");
                if (File.Exists(marker)) {
                    var p3 = Path.Combine(probe.FullName, relativePath);
                    if (File.Exists(p3)) return p3;
                }
                probe = probe.Parent;
            }

            throw new FileNotFoundException($"Unable to resolve use-case file: {relativePath}");
        }
    }
}
