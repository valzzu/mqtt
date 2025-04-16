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

var mqttFactory = new MqttServerFactory();

// #if SSL
var currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
#pragma warning disable SYSLIB0057 // Type or member is obsolete
var certificate = new X509Certificate2(Path.Combine(currentPath, "certificate.pfx"), "large4cats", X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057 // Type or member is obsolete

var mqttServerOptions = new MqttServerOptionsBuilder()
    .WithoutDefaultEndpoint() // This call disables the default unencrypted endpoint on port 1883
    .WithEncryptedEndpoint()
    .WithEncryptedEndpointPort(8883)
    .WithEncryptionCertificate(certificate.Export(X509ContentType.Pfx))
    .WithEncryptionSslProtocol(SslProtocols.Tls12)
    .Build();
    Log.Logger.Information("Using SSL certificate for MQTT server");

// If you want to use a non-encrypted MQTT server, you can uncomment the following lines instead of the above
// var mqttServerOptions = new MqttServerOptionsBuilder()
//     .WithDefaultEndpoint()
//     .WithDefaultEndpointPort(1883)
//     .Build();
//     Log.Logger.Information("Using unencrypted MQTT server");
// #endif

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(new RenderedCompactJsonFormatter())
    // .WriteTo.File(new RenderedCompactJsonFormatter(), "log.json", rollingInterval: RollingInterval.Hour) // File logging can be enabled if needed
    .CreateLogger();

using var mqttServer = mqttFactory.CreateMqttServer(mqttServerOptions);

static Data? DecryptMeshPacket(ServiceEnvelope serviceEnvelope)
{
    var nonce = new NonceGenerator(serviceEnvelope.Packet.From, serviceEnvelope.Packet.Id).Create();

    var decrypted = PacketEncryption.TransformPacket(serviceEnvelope.Packet.Encrypted.ToByteArray(), nonce, Resources.DEFAULT_PSK);
    var payload = Data.Parser.ParseFrom(decrypted);

    if (payload.Portnum > PortNum.UnknownApp && payload.Payload.Length > 0)
        return payload;

    // Was not able to decrypt the payload
    return null;
}

mqttServer.InterceptingPublishAsync += async (args) =>
{
    try 
    {
        if (args.ApplicationMessage.Payload.Length == 0)
        {
            Log.Logger.Warning("Received empty payload on topic {@Topic} from {@ClientId}", args.ApplicationMessage.Topic, args.ClientId);
            args.ProcessPublish = false; // This will block empty packets
            return;
        }
        var serviceEnvelope = ServiceEnvelope.Parser.ParseFrom(args.ApplicationMessage.Payload);

        // Block malformed service envelopes / packets
        if (
            String.IsNullOrWhiteSpace(serviceEnvelope.ChannelId) || 
            String.IsNullOrWhiteSpace(serviceEnvelope.GatewayId) ||
            serviceEnvelope.Packet == null ||
            serviceEnvelope.Packet.Id < 1 ||
            serviceEnvelope.Packet.From < 1 ||
            serviceEnvelope.Packet.Encrypted == null ||
            serviceEnvelope.Packet.Encrypted.Length < 1 ||
            serviceEnvelope.Packet.Decoded != null)
        {
            Log.Logger.Warning("Service envelope or packet is malformed. Blocking packet on topic {@Topic} from {@ClientId}", args.ApplicationMessage.Topic, args.ClientId);
            args.ProcessPublish = false;
            return;
        }

        var data = DecryptMeshPacket(serviceEnvelope);
        // If we were not able to decrypt the packet, it is likely encrypted with an unknown PSK
        // Uncomment the following lines if you want to block these packets 
        // if (data == null)
        // {
        //     Log.Logger.Warning("Service envelope does not contain a valid packet. Blocking packet");
        //     args.ProcessPublish = false; // This will block packets that are not valid protobuf packets
        //     return;
        // }

        if (data?.Portnum == PortNum.TextMessageApp)
        {
            Log.Logger.Information("Received text message on topic {@Topic} from {@ClientId}: {@Message}", 
                args.ApplicationMessage.Topic, args.ClientId, data.Payload.ToStringUtf8());
        }
        else
        {
            Log.Logger.Information("Received packet on topic {@Topic} from {@ClientId} with port number: {@Portnum}", 
                args.ApplicationMessage.Topic, args.ClientId, data?.Portnum);
        }

        // Any further validation logic to block a packet can be added here
        args.ProcessPublish = true;
    }
    catch (InvalidProtocolBufferException)
    {
        Log.Logger.Warning("Failed to decode presumed protobuf packet. Blocking");
        args.ProcessPublish = false; // This will block packets encrypted on unknown PSKs
    }
    catch (Exception ex)
    {
        Log.Logger.Error("Exception occured while attempting to decode packet on {@Topic} from {@ClientId}: {@Exception}", args.ApplicationMessage.Topic, args.ClientId, ex.Message);
        args.ProcessPublish = false; // This will block packets that caused us to encounter an exception
    }
};

mqttServer.InterceptingSubscriptionAsync += (args) =>
{
    args.ProcessSubscription = true; // Subscription filtering logic can be added here to only allow certain topics

    return Task.CompletedTask;
};

mqttServer.ValidatingConnectionAsync += (args) =>
{
    args.ReasonCode = true ? // Authentication logic can be added here
        MqttConnectReasonCode.Success : MqttConnectReasonCode.BadUserNameOrPassword;

    // You can block connections based on client ID, username, ip, etc.
    return Task.CompletedTask;
};

static IHostBuilder CreateHostBuilder(string[] args)
{
    return Host.CreateDefaultBuilder(args)
        .UseConsoleLifetime()
        .ConfigureServices((hostContext, services) =>
        {
            services
                .AddSingleton(Console.Out);
        });
}


using var host = CreateHostBuilder(args).Build();
await host.StartAsync();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
await mqttServer.StartAsync();

var ended = new ManualResetEventSlim();
var starting = new ManualResetEventSlim();

AssemblyLoadContext.Default.Unloading += ctx =>
{
    starting.Set();
    Log.Logger.Debug("Waiting for completion");
    ended.Wait();
};

starting.Wait();

Log.Logger.Debug("Received signal gracefully shutting down");
await mqttServer.StopAsync();
Thread.Sleep(1000);
ended.Set();

lifetime.StopApplication();
await host.WaitForShutdownAsync();
