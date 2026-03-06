using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Services;
using Haley.Utils;
using System;
using System.Text;
using System.Threading.Tasks;

namespace Haley.Utils {
    public static class WFEngineExtensions {
        public static async Task<IWorkFlowEngine> Build(this WorkFlowEngineMaker input, IAdapterGateway agw)  {
            //replace the sql contents, as only we know that.
            var adapterKey = await input.Initialize(agw); //Base names are already coming from the concrete implementation of DBInstanceMaker
            var dal = new MariaWorkFlowDAL(agw, adapterKey);
            return new WorkFlowEngine(dal, input.Options);
        }
    }
}
