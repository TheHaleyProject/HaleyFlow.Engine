using System;

namespace WFE.Test {
    internal class UseSettingsBase  {
        public int EnvCode { get; set; } = 1000;
        public string EnvDisplayName { get; set; } = "dev";
        public string ConsumerGuid { get; set; } = "89c52807-5054-47fc-9dee-dbb8b42218cb";
        public string EngineConString { get; set; } =
          "server=127.0.0.1;port=3306;user=root;password=admin@456$;database=wfengine_testcases;Allow User Variables=true;GuidFormat=None";

        public string ConsumerConString { get; set; } =
          "server=127.0.0.1;port=3306;user=root;password=admin@456$;database=wfconsumer_testcases;Allow User Variables=true;GuidFormat=None";

        public TimeSpan MonitorInterval { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan AckPendingResendAfter { get; set; } = TimeSpan.FromSeconds(20);
        public TimeSpan AckDeliveredResendAfter { get; set; } = TimeSpan.FromSeconds(30);
        public int MaxRetryCount { get; set; } = 10;
        public int ConsumerTtlSeconds { get; set; } = 30;
        public int ConsumerDownRecheckSeconds { get; set; } = 10;

        public int ConsumerBatchSize { get; set; } = 20;
        public TimeSpan ConsumerPollInterval { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan ConsumerHeartbeatInterval { get; set; } = TimeSpan.FromSeconds(3);
        public TimeSpan WaitAfterTrigger { get; set; } = TimeSpan.FromSeconds(4);
    }
}

