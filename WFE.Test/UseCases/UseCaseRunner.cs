using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WFE.Test.UseCases.ChangeRequest;
using WFE.Test.UseCases.LoanApproval;
using WFE.Test.UseCases.PaperlessReview;
using WFE.Test.UseCases.VendorRegistration;

namespace WFE.Test.UseCases {
    internal static class UseCaseRunner {
        static readonly Dictionary<string, Func<IWorkflowUseCase>> Cases = new(StringComparer.OrdinalIgnoreCase) {
            ["vendor-registration"] = static () => new VendorRegistrationUseCase(),
            ["loan-approval"] = static () => new LoanApprovalUseCase(),
            ["paperless-review"] = static () => new PaperlessReviewUseCase(),
            ["change-request"] = static () => new ChangeRequestUseCase()
        };

        public static async Task RunAsync(string[] args) {
            var useCaseName = args.Length > 0
                ? (args[0] ?? string.Empty).Trim()
                : "vendor-registration";

            if (!Cases.TryGetValue(useCaseName, out var factory)) {
                Console.WriteLine($"Unknown use-case '{useCaseName}'.");
                Console.WriteLine("Available use-cases:");
                foreach (var key in Cases.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)) {
                    Console.WriteLine($"  - {key}");
                }
                return;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => {
                e.Cancel = true;
                cts.Cancel();
            };

            var useCase = factory();
            Console.WriteLine($"Running use-case: {useCase.Name}");
            Console.WriteLine(useCase.Description);
            Console.WriteLine("Press Ctrl+C to stop.\n");

            await useCase.RunAsync(cts.Token);
        }
    }
}
