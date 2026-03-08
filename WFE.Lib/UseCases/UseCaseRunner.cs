using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WFE.Test.UseCases {
    public static class UseCaseRunner {
        public static async Task RunAsync(params string[] args) {
            var useCaseName = args.Length > 0
                ? (args[0] ?? string.Empty).Trim()
                : "vendor-registration";

            if (!SharedUseCaseHost.TryGetDescription(useCaseName, out var description)) {
                Console.WriteLine($"Unknown use-case '{useCaseName}'.");
                Console.WriteLine("Available use-cases:");
                foreach (var key in SharedUseCaseHost.GetUseCaseKeys().OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) {
                    Console.WriteLine($"  - {key}");
                }
                return;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine($"Running use-case: {useCaseName}");
            Console.WriteLine(description);
            Console.WriteLine("Press Ctrl+C to stop.\n");

            await SharedUseCaseHost.RunAsync(useCaseName, cts.Token);
        }
    }
}
