using Haley.Abstractions;
using Haley.Models;
using static Haley.Internal.QueryFields;
using Haley.Internal;

namespace Haley.Utils {
    public class MariaWorkFlowDAL : MariaWorkFlowDALUtil, IWorkFlowDAL {
        public IBlueprintReadDAL Blueprint { get; }
        public IInstanceDAL Instance { get; }
        public ILifeCycleDAL LifeCycle { get; }
        public ILifeCycleDataDAL LifeCycleData { get; }
        public IHookDAL Hook { get; }

        public IAckDAL Ack { get; }
        public IAckConsumerDAL AckConsumer { get; }
        public ILcAckDAL LcAck { get; }
        public IHookAckDAL HookAck { get; }
        public IAckDispatchDAL AckDispatch { get; }

        public IActivityDAL Activity { get; }
        public IActivityStatusDAL ActivityStatus { get; }

        public IRuntimeDAL Runtime { get; }
        public IRuntimeDataDAL RuntimeData { get; }

        public MariaWorkFlowDAL(IAdapterGateway agw, string key) : base(agw, key) {
            Blueprint = new MariaBlueprintReadDAL(this);
            Instance = new MariaInstanceDAL(this);
            LifeCycle = new MariaLifeCycleDAL(this);
            LifeCycleData = new MariaLifeCycleDataDAL(this);
            Hook = new MariaHookDAL(this);

            Ack = new MariaAckDAL(this);
            AckConsumer = new MariaAckConsumerDAL(this);
            LcAck = new MariaLcAckDAL(this);
            HookAck = new MariaHookAckDAL(this);
            AckDispatch = new MariaAckDispatchDAL(this);

            Activity = new MariaActivityDAL(this);
            ActivityStatus = new MariaActivityStatusDAL(this);

            Runtime = new MariaRuntimeDAL(this);
            RuntimeData = new MariaRuntimeDataDAL(this);
        }
    }
}
