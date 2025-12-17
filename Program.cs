using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.Sqlite;

int port = 5000;
string dbPath = "Data Source=database1.db";

PrepareDatabase(dbPath);

TcpListener server = new TcpListener(IPAddress.Any, port);
server.Start();
Console.WriteLine($"Sunucu çalışıyor (Port: {port})...");

while (true)
{
    TcpClient client = server.AcceptTcpClient();

    Task.Run(() => HandleClient(client, dbPath));
}

void HandleClient(TcpClient client, string connectionString)
{
    using (client)
    using (NetworkStream stream = client.GetStream())
    using (var reader = new StreamReader(stream, Encoding.UTF8))
    using (var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
    using (var writerGetSet = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true })
    using (var writeUyarı = new StreamWriter(stream, Encoding.UTF8))
    {
        Console.WriteLine("İstemci bağlandı.");
        string read()
        {
            writerGetSet.WriteLine("Yapmak İstediğiniz İşlem :(GET/SET)");
            string? process = reader.ReadLine();
            return process;
        }
        string process = read();

        processControl(process);
        string processControl(string? process)
        {
            if (process != null)
            {
                switch (process)
                {
                    case "GET":

                        writer.WriteLine("Mesaj ID :");
                        string? messageId = reader.ReadLine();
                        if (!string.IsNullOrEmpty(messageId))
                        {
                            GET(connectionString, messageId, writer);
                        }
                        return processControl(read());
                    case "SET":

                        SET(writer, reader);
                        return "SET";
                    default:
                        writeUyarı.WriteLine("Lütfen geçerli bir işlem giriniz(GET/SET)");
                        string? process2 = reader.ReadLine();
                        return processControl(process2);

                }
            }
            else
            {

                if (process == null)
                {

                    return "DISCONNECTED";
                }


                writeUyarı.WriteLine("Lütfen geçerli bir işlem giriniz(GET/SET)");
                string? process3 = reader.ReadLine();
                return processControl(process3);
            }
            void SET(StreamWriter writer, StreamReader reader)
            {


                while (true)
                {
                    writer.WriteLine("Mesaj ID :");

                    string? messageId = reader.ReadLine();
                    if (string.IsNullOrEmpty(messageId)) return;

                    writer.WriteLine("Mesajınızı Giriniz : ");
                    string? msg = reader.ReadLine();

                    if (msg == null) break;

                    Console.WriteLine($"[{messageId}]: {msg}");


                    SaveToSql(connectionString, messageId, msg);

                    writer.WriteLine("SERVER_OK: Mesaj veritabanına kaydedildi.");

                    string process = read();

                    processControl(process);

                }
            }

        }
        Console.WriteLine("İstemci ayrıldı.");






    }
}
    

    void PrepareDatabase(string connectionString)
    {
    using (var connection = new SqliteConnection(connectionString))
    {
        connection.Open();
        string tableCmd = @"CREATE TABLE IF NOT EXISTS Messages (
            MessageId TEXT UNIQUE, 
            Content TEXT
        )";

        using (var command = connection.CreateCommand())
        {
            command.CommandText = tableCmd;
            command.ExecuteNonQuery();
        }
    }
    }

void SaveToSql(string connectionString, string messageId, string content)
{
    using (var connection = new SqliteConnection(connectionString))
    {
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT OR IGNORE INTO Messages (MessageId, Content) VALUES ($id, $content)";
            command.Parameters.AddWithValue("$id", messageId);
            command.Parameters.AddWithValue("$content", content);
            command.ExecuteNonQuery();
        }
    }
}



void GET(string connectionString, string targetId , StreamWriter writer) 
{
    try
    {
        using (var connection = new SqliteConnection(connectionString))

        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Content FROM Messages WHERE MessageId = $id";
                command.Parameters.AddWithValue("$id", targetId);

                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                    
                        string content = reader.GetString(0);

                        Console.WriteLine($"\n[BAŞARILI] ID: {targetId} bulundu.");
                        writer.WriteLine($"İçerik: {content}");
                    }
                    else
                    {
                        writer.WriteLine($"\n[UYARI] {targetId} ID'li bir mesaj bulunamadı.");
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        writer.WriteLine("SQL Hatası: " + ex.Message);
    }
    
}
