using System;

namespace WFE.Test.UseCases.ChangeRequest {
    public sealed class ChangeRequestUseCaseSettings : UseSettingsBase {
        public const string DefinitionNameConst = "ProjectChangeRequest";
        public string DefName { get; set; } = DefinitionNameConst;
    }
}
