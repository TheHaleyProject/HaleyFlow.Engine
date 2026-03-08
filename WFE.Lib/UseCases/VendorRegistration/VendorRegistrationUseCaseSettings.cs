using System;

namespace WFE.Test.UseCases.VendorRegistration {
    public sealed class VendorRegistrationUseCaseSettings : UseSettingsBase {
        public const string DefinitionNameConst = "VendorRegistration";
        public string DefName { get; set; } = DefinitionNameConst;
    }
}

