using System;

namespace WFE.Test.UseCases.PaperlessReview {
    public sealed class PaperlessReviewUseCaseSettings : UseSettingsBase {
        public const string DefinitionNameConst = "PaperlessReview";
        public string DefName { get; set; } = DefinitionNameConst;
    }
}
