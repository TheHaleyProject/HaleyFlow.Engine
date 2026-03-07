using System;

namespace WFE.Test {
    internal class UseSettingsBase  {
        public int EnvCode { get; set; } = 1000;
        public string EnvDisplayName { get; set; } = "dev";
        public string ConsumerGuid { get; set; } = "89c52807-5054-47fc-9dee-dbb8b42218cb";
          // Number of timed entity creation attempts (0 = disabled).
        public int RandomEntityCount { get; set; } = 0;

        // Delay between each creation attempt.
        public TimeSpan RandomEntityInterval { get; set; } = TimeSpan.FromSeconds(10);

        // Keep engine + consumer running until user exits from console.
        public bool KeepAliveAfterRun { get; set; } = true;

        // Console command to terminate when KeepAliveAfterRun is enabled.
        public string ExitCommand { get; set; } = "exit";

        // For Y/N prompts, auto-select "Yes" when no input is provided within this duration.
        public TimeSpan ConfirmationTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan MonitorInterval { get; set; } = TimeSpan.FromSeconds(8);
        public TimeSpan AckPendingResendAfter { get; set; } = TimeSpan.FromSeconds(20);
        public TimeSpan AckDeliveredResendAfter { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxRetryCount { get; set; } = 10;
        public int ConsumerTtlSeconds { get; set; } = 30;
        public int ConsumerDownRecheckSeconds { get; set; } = 10;

        public int ConsumerBatchSize { get; set; } = 20;
        public TimeSpan ConsumerPollInterval { get; set; } = TimeSpan.FromSeconds(2);
        public TimeSpan ConsumerHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan WaitAfterTrigger { get; set; } = TimeSpan.FromSeconds(200);
    }
}

