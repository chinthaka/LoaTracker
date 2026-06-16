using Azure.Provisioning.Storage;

var builder = DistributedApplication.CreateBuilder(args);

//var apiService = builder.AddProject<Projects.LoaTracker_ApiService>("apiservice")
//    .WithHttpHealthCheck("/health");

//builder.AddProject<Projects.LoaTracker_Web>("webfrontend")
//    .WithExternalHttpEndpoints()
//    .WithHttpHealthCheck("/health")
//    .WithReference(apiService)
//    .WaitFor(apiService);

// Storage emulator (Azurite in container) - provides Queues and Tables locally
var storage = builder
	.AddAzureStorage("storage")
	.RunAsEmulator(emulator =>
	{
		emulator.WithBlobPort(10000)
				.WithQueuePort(10001)
				.WithTablePort(10002);
				//.WithLifetime(ContainerLifetime.Persistent)
				//.WithDataVolume();

		
		IEnumerable<EndpointAnnotation> endpoints = emulator.Resource.Annotations
			.OfType<EndpointAnnotation>()
			.Where(endpoint => endpoint.Name is "blob" or "queue" or "table");

		foreach (EndpointAnnotation endpoint in endpoints)
		{
			endpoint.IsProxied = false;
		}

	});

var queues = storage.AddQueues("queues");
var tables = storage.AddTables("tables");


// Api porject - reference both queue and table connections
var api = builder.AddProject<Projects.LoaTracker_ApiService>("api")
	.WithReference(queues)
	.WithReference(tables)
	.WaitFor(storage);

//queues.AddQueue("loa-requests");


builder.AddAzureFunctionsProject<Projects.LoaTracker_Functions>("functions")
	.WithHostStorage(storage)                                     
	.WithEnvironment("queues", "UseDevelopmentStorage=true").WaitFor(queues)
	.WithEnvironment("tables", "UseDevelopmentStorage=true").WaitFor(tables)
	.WaitFor(storage);


builder.Build().Run();
