using System;

namespace WFE.Test.UseCases.ChangeRequest {
    internal sealed class ChangeRequestUseCaseSettings : UseSettingsBase {
        public const string DefinitionNameConst = "ProjectChangeRequest";
        public string DefName { get; set; } = DefinitionNameConst;
    }
}
