using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GrpcService.Roles;


const int leaderPort = 5555;
var backUp = GetBackUpData();




IServer server = IsPortFree(5555)
    ? new Leader(backUp)
    : new Member();
try
{
    await server.Run();
}
catch
{
    if (!IsPortFree(leaderPort))
    {
        for (var i = 0; i < 5; ++i)
        {
            try
            {
                server = new Member();
                await server.Run();
                break;
            }
            catch
            {
                // ignored
            }
        }
    }
}

return;
bool IsPortFree(int port)
{
    var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
    var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
    return tcpConnInfoArray.All(endpoint => endpoint.Port != port);
}

static int GetBackUpData()
{
    var projectRoot =
        Directory.GetParent(AppContext.BaseDirectory)!.Parent!.Parent!.Parent!.FullName;

    var path = Path.Combine(projectRoot, "tolerance.conf");


    if (!File.Exists(path))
    {
        Console.WriteLine("File is not found");
        throw new FileNotFoundException("tolerance.conf not found", path);
    }
    var lines = File.ReadAllLines(path);
    var parts = lines[0].Split('=', 2);
    return int.Parse(parts[1]);
}
