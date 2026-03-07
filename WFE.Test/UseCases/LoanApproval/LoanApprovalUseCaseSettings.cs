using System;

namespace WFE.Test.UseCases.LoanApproval {
    internal sealed class LoanApprovalUseCaseSettings : UseSettingsBase {
        public const string DefinitionNameConst = "LoanApproval";
        public string DefName { get; set; } = DefinitionNameConst;
    }
}
