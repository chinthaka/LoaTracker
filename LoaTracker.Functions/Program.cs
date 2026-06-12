using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SendGrid;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();


builder.Services.AddSingleton<ISendGridClient>(sp =>
{
    var key = builder.Configuration["SendGridApiKey"] ?? throw new InvalidOperationException("SendGridApiKey not configured");
    return new SendGridClient(key);
});

builder.Build().Run();
