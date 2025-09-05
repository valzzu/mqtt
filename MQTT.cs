using MQTTnet.Server;
using Meshtastic.Protobufs;
using Google.Protobuf;
using Serilog;
using MQTTnet.Protocol;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog.Formatting.Compact;
using Meshtastic.Crypto;
using Meshtastic;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Buffers;
using System.Collections;
using System.Data;
using Meshtastic.Mqtt;
using Npgsql;

var connString = "Host=db;Port=5332;Username=admin;Password=admin;Database=meshtastic";
await using var dataSource  =  NpgsqlDataSource.Create(connString);

var webServer = new WebServer();
await RunMqttServer(args);
async Task RunMqttServer(string[] args)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .WriteTo.Console(new RenderedCompactJsonFormatter())
        // .WriteTo.File(new RenderedCompactJsonFormatter(), "log.json", rollingInterval: RollingInterval.Hour)
        .CreateLogger();

    using var mqttServer = new MqttServerFactory()
        .CreateMqttServer(BuildMqttServerOptions());
    ConfigureMqttServer(mqttServer);

    // Set up host
    using var host = CreateHostBuilder(args).Build();
    await host.StartAsync();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

    await mqttServer.StartAsync();
    
    Console.WriteLine("MQTT server started");
    
    webServer.Start();

    
    await SetupGracefulShutdown(mqttServer, webServer, lifetime, host);
    

}

MqttServerOptions BuildMqttServerOptions()
{
    var currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

    //#pragma warning disable SYSLIB0057 // Type or member is obsolete
    //var certificate = new X509Certificate2(
    //    Path.Combine(currentPath, "certificate.pfx"),
    //    "large4cats",
    //    X509KeyStorageFlags.Exportable);
    //#pragma warning restore SYSLIB0057

    //var options = new MqttServerOptionsBuilder()
    //    .WithoutDefaultEndpoint()
    //    .WithEncryptedEndpoint()
    //    .WithEncryptedEndpointPort(8883)
    //    .WithEncryptionCertificate(certificate.Export(X509ContentType.Pfx))
    //    .WithEncryptionSslProtocol(SslProtocols.Tls12)
    //    .Build();

    var options = new MqttServerOptionsBuilder()
        .WithDefaultEndpoint()
        .WithDefaultEndpointPort(1883)
        .Build();


    //Log.Logger.Information("Using SSL certificate for MQTT server");
    return options;
}

void ConfigureMqttServer(MqttServer mqttServer)
{
    mqttServer.InterceptingPublishAsync += HandleInterceptingPublish;
    mqttServer.InterceptingSubscriptionAsync += HandleInterceptingSubscription;
    mqttServer.ValidatingConnectionAsync += HandleValidatingConnection;
}

async Task HandleInterceptingPublish(InterceptingPublishEventArgs args)
{
    try 
    {
        if (args.ApplicationMessage.Payload.Length == 0)
        {
            Log.Logger.Warning("Received empty payload on topic {@Topic} from {@ClientId}", args.ApplicationMessage.Topic, args.ClientId);
            args.ProcessPublish = false;
            return;
        }

        var serviceEnvelope = ServiceEnvelope.Parser.ParseFrom(args.ApplicationMessage.Payload);

        if (!IsValidServiceEnvelope(serviceEnvelope))
        {
            Log.Logger.Warning("Service envelope or packet is malformed. Blocking packet on topic {@Topic} from {@ClientId}",
                args.ApplicationMessage.Topic, args.ClientId);
            args.ProcessPublish = false;
            return;
        }

        // Spot for any async operations we might want to perform
        await Task.FromResult(0);




       

        var data = DecryptMeshPacket(serviceEnvelope);

        // Console.WriteLine($"Serviceenvelope: {serviceEnvelope}");
        //
        // Console.WriteLine($"Decrypted packet: {data}");



        if (serviceEnvelope.ChannelId == "LongFast" && serviceEnvelope.Packet?.HopLimit > 0)
        {
            //zero hopping for longfast
            Log.Logger.Debug("LongFast packet detected, setting hoplimit to 0");
            serviceEnvelope.Packet.HopLimit = 0;

            args.ApplicationMessage.Payload = new ReadOnlySequence<byte>(serviceEnvelope.ToByteArray());

        }
        var data2 = DecryptMeshPacket(serviceEnvelope);
        // Console.WriteLine($"Serviceenvelope: {serviceEnvelope}");
        //
        // Console.WriteLine($"Decrypted packet: {data2}");


        // uncomment to block unrecognized packets
        if (data == null)
        {
            Log.Logger.Warning("service envelope does not contain a valid packet. blocking packet");
            args.ProcessPublish = false;
            return;
        }

        LogReceivedMessage(args.ApplicationMessage.Topic, args.ClientId, data);
        args.ProcessPublish = true;
    }
    catch (InvalidProtocolBufferException)
    {
        Log.Logger.Warning("Failed to decode presumed protobuf packet. Blocking");
        args.ProcessPublish = false;
    }
    catch (Exception ex)
    {
        Log.Logger.Error("Exception occurred while processing packet on {@Topic} from {@ClientId}: {@Exception}",
            args.ApplicationMessage.Topic, args.ClientId, ex.Message);
        args.ProcessPublish = false;
    }
}

Task HandleInterceptingSubscription(InterceptingSubscriptionEventArgs args)
{
    // Add filtering logic here if needed
    args.ProcessSubscription = true;
    return Task.CompletedTask;
}

Task HandleValidatingConnection(ValidatingConnectionEventArgs args)
{
    //block unwated connections eg. shellyplus1pm-*
    if (args.ClientId.StartsWith("shellyplus1pm-", StringComparison.OrdinalIgnoreCase))
    {
        args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
        //Log.Logger.Information("Connection rejected: Client {ClientId} is a Shelly Plus 1PM device", args.ClientId);
        return Task.CompletedTask;
    }
    if (args.ClientId == "2Ujeregc9mQgWt2IBOzzaM")
    {
        args.ReasonCode = MqttConnectReasonCode.NotAuthorized;
        return Task.CompletedTask;
    }

    if (args.UserName == "meshdev" && args.Password == "large4cats")
    {
        args.ReasonCode = MqttConnectReasonCode.Success;
        Log.Logger.Information("client {@ID} connected succesfully ", args.ClientId);

        return Task.CompletedTask;
    }


    args.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;

    Log.Logger.Information("client {@ID} failed to connect with reason ", args.ClientId, args.ReasonCode);

    return Task.CompletedTask;
}


bool IsValidServiceEnvelope(ServiceEnvelope serviceEnvelope)
{
    return !(String.IsNullOrWhiteSpace(serviceEnvelope.ChannelId) ||
            String.IsNullOrWhiteSpace(serviceEnvelope.GatewayId) ||
            serviceEnvelope.Packet == null ||
            serviceEnvelope.Packet.Id < 1 ||
            serviceEnvelope.Packet.From < 1 ||
            serviceEnvelope.Packet.Encrypted == null ||
            serviceEnvelope.Packet.Encrypted.Length < 1 ||
            serviceEnvelope.Packet.Decoded != null);
}

void LogReceivedMessage(string topic, string clientId, Data? data)
{
    if (data?.Portnum == PortNum.TextMessageApp)
    {
        Log.Logger.Information("Received text message on topic {@Topic} from {@ClientId}: {@Message}",
            topic, clientId, data.Payload.ToStringUtf8());
    }
    else
    {
        Log.Logger.Information("Received packet on topic {@Topic} from {@ClientId} with port number: {@Portnum}",
            topic, clientId, data?.Portnum);
    }
    Console.WriteLine(data?.Portnum);
    
    
}

static Data? DecryptMeshPacket(ServiceEnvelope serviceEnvelope)
{
    var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();
    var decrypted = PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToByteArray(), nonce, Resources.DEFAULT_PSK);
    var payload = Data.Parser.ParseFrom(decrypted);

    if (payload.Portnum > PortNum.UnknownApp && payload.Payload.Length > 0)
        return payload;

    return null;
}


async Task SetupGracefulShutdown(MqttServer mqttServer, WebServer webServer, IHostApplicationLifetime lifetime, IHost host)
{
    
    lifetime.ApplicationStopping.Register(() =>
    {
        Log.Logger.Information("Graceful shutdown initiated");

        try
        {

            // Stop MQTT server
            mqttServer.StopAsync().GetAwaiter().GetResult();
            Console.WriteLine("MQTT server stopped");

            // Stop WebServer
            webServer.Stop();
            Console.WriteLine("WebServer stopped");
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Error during shutdown");
        }
    });
    await host.WaitForShutdownAsync();
    
}


static IHostBuilder CreateHostBuilder(string[] args)
{
    var cs = "Host=db:5432;Username=admin;Password=admin;Database=meshtastic";
    return Host.CreateDefaultBuilder(args)
        .UseConsoleLifetime()
        .ConfigureServices((hostContext, services) =>
        {
            services.AddSingleton(Console.Out);
            services.AddHostedService(sp => new UptimeHostedService(cs));
        });
}
