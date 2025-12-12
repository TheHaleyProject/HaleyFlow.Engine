using Haley;
using Haley.Utils;

Console.WriteLine("Hello, World!");
var response = await LifeCycleInitializer.InitializeAsync(new AdapterGateway(), "lcstate");
Console.ReadKey();
