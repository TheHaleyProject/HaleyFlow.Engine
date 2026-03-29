using Haley.Abstractions;
using Haley.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace WFE.Test.UseCases.LoanApproval {
    [LifeCycleDefinition(LoanApprovalWrapper.DefinitionNameConst)]
    public sealed class LoanApprovalRelay : WorkflowRelayBase {
        protected override string DefinitionJson => File.ReadAllText(ResolvePath("definition.loan_approval.json"));
        protected override string? PolicyJson    => File.ReadAllText(ResolvePath("policy.loan_approval.json"));

        protected override void ConfigureRelay(WorkflowRelay relay) {
            relay.On(2000, ctx => {
                Console.WriteLine($"[RELAY] LoanApproval AutoStart entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.KYC.CHECK", ctx => {
                Console.WriteLine($"[RELAY] KYC Check entity={ctx.EntityRef} → passed");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.KYC.AUDIT.LOG", ctx => {
                Console.WriteLine($"[RELAY] KYC Audit Log entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.KYC.NOTIFY.APPLICANT", ctx => {
                Console.WriteLine($"[RELAY] KYC Notify Applicant entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.CREDIT.CHECK", ctx => {
                Console.WriteLine($"[RELAY] Credit Check entity={ctx.EntityRef} → accepted");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.CREDIT.LOCAL.BACKUP", ctx => {
                Console.WriteLine($"[RELAY] Credit Backup entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.CREDIT.RISK.FEED", ctx => {
                Console.WriteLine($"[RELAY] Credit Risk Feed entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.RISK.CHECK", ctx => {
                Console.WriteLine($"[RELAY] Risk Check entity={ctx.EntityRef} → accepted");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.RISK.AUDIT.LOG", ctx => {
                Console.WriteLine($"[RELAY] Risk Audit Log entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.RISK.NOTIFY.ANALYST", ctx => {
                Console.WriteLine($"[RELAY] Risk Notify Analyst entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.MANAGER.DECISION", ctx => {
                Console.WriteLine($"[RELAY] Manager Decision entity={ctx.EntityRef} → approved");
                //throw new Exception("No reason");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.MANAGER.DOC.ARCHIVE", ctx => {
                Console.WriteLine($"[RELAY] Manager Doc Archive entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.MANAGER.NOTIFY.APPLICANT", ctx => {
                Console.WriteLine($"[RELAY] Manager Notify Applicant entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.MANAGER.REMINDER", ctx => {
                Console.WriteLine($"[RELAY] Manager Reminder entity={ctx.EntityRef}");
                throw new Exception("No reason");
                return Task.FromResult(true);
            });

            relay.OnHook("APP.LOAN.MANAGER.REMINDER.EMAIL", ctx => {
                Console.WriteLine($"[RELAY] Manager Reminder Email entity={ctx.EntityRef}");
                return Task.FromResult(true);
            });
        }

        private static string ResolvePath(string fileName) {
            var candidates = new[] {
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WFE.Lib", "UseCases", "LoanApproval", fileName),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "WFE.Lib", "UseCases", "LoanApproval", fileName),
                Path.Combine(AppContext.BaseDirectory, "UseCases", "LoanApproval", fileName),
            };
            foreach (var path in candidates) {
                var full = Path.GetFullPath(path);
                if (File.Exists(full)) return full;
            }
            throw new FileNotFoundException($"Unable to locate relay JSON file '{fileName}'.");
        }
    }
}
