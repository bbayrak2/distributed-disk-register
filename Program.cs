using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

int port = 5000;
TcpListener server = new TcpListener(IPAddress.Any, 5000);
server.Start();

Console.WriteLine("Sunucu çalışıyor...");

while (true)
{
    TcpClient client = server.AcceptTcpClient();
    Console.WriteLine("İstemci bağlandı.");

    NetworkStream stream = client.GetStream();
    byte[] buffer = new byte[1024];

    while (true)
    {
        int count = stream.Read(buffer, 0, buffer.Length);
        if (count == 0)
        {
            Console.WriteLine("İstemci bağlantıyı kapattı.");
            break;
        }

        string msg = Encoding.UTF8.GetString(buffer, 0, count);
        Console.WriteLine("Gelen: " + msg);

        File.AppendAllText("messages.txt", msg + Environment.NewLine);
    }

    client.Close();
}
