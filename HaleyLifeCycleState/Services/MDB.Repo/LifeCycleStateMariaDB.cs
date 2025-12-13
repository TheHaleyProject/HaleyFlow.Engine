using Haley.Abstractions;
using Haley.Enums;
using Haley.Internal;
using Haley.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static Haley.Internal.QueryFields;

namespace Haley.Services {
    public partial class LifeCycleStateMariaDB : ILifeCycleStateRepository {
        readonly IAdapterGateway _agw;
        readonly ILogger _logger;
        readonly string _key;
        readonly bool _throwExceptions;
        readonly ConcurrentDictionary<string, (ITransactionHandler handler, DateTime created)> _handlers = new(); //In case we decide to use any transactions inside, we can track them here and then later, after all transaction operations are completed, we can finally commit them in one place. We use the string (preferably a GUID) as the key to identify the transaction.

        public bool ThrowExceptions => _throwExceptions;
        static object DbNull(object? v) => v ?? DBNull.Value;

        public LifeCycleStateMariaDB(IAdapterGateway agw, string key, ILogger logger, bool throwExceptions = false) {
            _agw = agw;
            _key = key;
            _logger = logger;
            _throwExceptions = throwExceptions;
        }
    }
}
