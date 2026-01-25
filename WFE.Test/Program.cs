using System;
using System.Threading;
using System.Threading.Tasks;
using WFE.Test;
using Haley.Utils;
using Haley.Models;
using Haley.Enums;
using Haley.Abstractions;

var settings = new VendorRegistrationConsoleAppSettings {
    EnvCode = 1,
    EnvDisplayName = "dev",
    DefName = "VendorRegistration",
    ConsumerGuid = "9d9dda78-3639-4a10-861e-1301472d59df",
    ConnectionString = "server=127.0.0.1;port=3306;user=root;password=admin@456$;database=testlcs;Allow User Variables=true;GuidFormat=None",
    DefinitionFileName = "vendor_registration.json",
    PolicyFileName = "vendor_registration_policies.json",
    AckRequired = true
};

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var app = new VendorRegistrationConsoleApp(settings);
await app.RunAsync(cts.Token);