using System.Net;
using System.Text;
using System.Threading;

namespace Meshtastic.Mqtt;


public class WebServer
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();

    public WebServer(string address = "http://localhost:8080/")
    {
        _listener = new HttpListener();
        _listener.Prefixes.Add(address);
    }

    public void Start()
    {
        _listener.Start();
        Console.WriteLine("WebServer listening...");

        Task.Run(() => ListenLoop(_cts.Token));
    }

    private async Task ListenLoop(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync();
                var request = context.Request;
                var response = context.Response;
                
                Console.WriteLine(request.Url?.AbsolutePath);

                string filePath = "";

                switch (request.Url?.AbsolutePath)
                {
                    case "/":
                        // Serve the HTML file
                        filePath = Path.Combine(AppContext.BaseDirectory, "public", "index.html");
                        
                        break;
                    case "/latest":
                    {
                        // Serve dynamic MQTT data (for example)
                        filePath = null;
                        string responseString = "Latest MQTT message: " + "reeee";
                        byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                        response.ContentType = "text/plain";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, token);
                        response.OutputStream.Close();
                        continue;
                    }
                }
                
                Console.WriteLine(filePath);

                if (File.Exists(filePath))
                {
                    string html = await File.ReadAllTextAsync(filePath, token);
                    byte[] buffer = Encoding.UTF8.GetBytes(html);
                    response.ContentType = "text/html";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length, token);
                }
                else
                {
                    response.StatusCode = 404;
                }

                response.OutputStream.Close();
            }
        }
        catch
        {
        }
    }


public void Stop()
    {
        _cts.Cancel();
        _listener.Stop();
    }
}