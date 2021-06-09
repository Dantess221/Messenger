using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using Lib;
using System.Threading;
using System.IO;

namespace Server
{
    class Server
    {
        const int PORT = 1774;
        private static TcpListener listener;
        private static List<ClientData> clients = new List<ClientData>();

        static void Main(string[] args)
        {
            clients = new List<ClientData>();

            Console.Title = "RSA Messenger | Сервер";

            StartServer();
        }

        private static void StartServer()
        {
            Console.WriteLine("Серевер запущен по адресу: " + Packet.GetThisIPv4Adress());

            listener = new TcpListener(new IPEndPoint(IPAddress.Parse(Packet.GetThisIPv4Adress()), PORT));

            Console.WriteLine("Сервер запущен. Ожидаю подключения клиентов...\n");

            Thread listenForNewClients = new Thread(ListenForNewClients);
            listenForNewClients.Start();
        }

        private static void ListenForNewClients()
        {
            listener.Start();

            while (true)
            {
                clients.Add(new ClientData(listener.AcceptTcpClient()));
                Console.WriteLine("Новый клиент зарегестрировался!");
            }            
        }

        public static void DataIn(object tcpClient)
        {
            TcpClient client = (TcpClient)tcpClient;
            NetworkStream clientStream = client.GetStream();
            try
            {
                while (true)
                {
                    byte[] buffer;
                    byte[] dataSize = new byte[4]; 

                    int readBytes = clientStream.Read(dataSize, 0, 4);

                    while (readBytes != 4)
                    {
                        readBytes += clientStream.Read(dataSize, readBytes, 4 - readBytes);
                    }
                    var contentLength = BitConverter.ToInt32(dataSize, 0);

                    buffer = new byte[contentLength];
                    readBytes = 0;
                    while (readBytes != buffer.Length)
                    {
                        readBytes += clientStream.Read(buffer, readBytes, buffer.Length - readBytes);
                    }

                    DataManagerForIncommingClientData(new Packet(buffer), client);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Номер: " + ex.ErrorCode + " Сообщение: " + ex.Message);
            }
            catch (IOException)
            {
                ClientData disconnectedClient = GetClientFromList(client);
                Console.WriteLine("Клиент отключился с UID: " + GetClientFromList(client).UID);
                clients.Remove(disconnectedClient);
                Console.WriteLine("Клиент удалён из списка.\n");

                foreach (ClientData c in clients)
                {
                    c.SendDataPacketToClient(new Packet(Packet.PacketType.ClientDisconnected, disconnectedClient.UID + ";" + disconnectedClient.PublicKey));
                    Console.WriteLine(c.UID + " Уведомил, что " + disconnectedClient.UID + " отключился");
                }
                Console.ReadLine();
                Environment.Exit(0);
            }
        }

        private static void DataManagerForIncommingClientData(Packet p, TcpClient clientSocket)
        {
            ClientData client;
            switch (p.type)
            {
                case Packet.PacketType.Registration:
                    Console.WriteLine("Клиент хочет зарегестрироватся как UID: " + p.uid + " с публичным паролем: " + p.publicKey);
                    client = GetClientFromList(clientSocket);

                    foreach (ClientData c in clients)
                    {
                        if(c.UID.ToLower() == p.uid.ToLower())
                        {
                            client.SendDataPacketToClient(new Packet(Packet.PacketType.RegistrationFail, "Пользователь с таким UID уже существует!"));
                        }
                    }
                    client.UID = p.uid;
                    client.PublicKey = p.publicKey;
                    client.SendDataPacketToClient(new Packet(Packet.PacketType.RegistrationSuccess));

                    foreach (ClientData c in clients)
                    {
                        if (c.UID != p.uid)
                        {
                            c.SendDataPacketToClient(new Packet(Packet.PacketType.ClientConnected, p.uid + ";" + p.publicKey));
                        }
                    }
                    break;
                case Packet.PacketType.GetClientList:
                    client = GetClientFromList(clientSocket);
                    Console.WriteLine("Клиент " + client.UID + " хочет получить список пользователей. Генерирую...");

                    List<object> dataList = new List<object>();
                    foreach (ClientData c in clients)
                    {
                        if (c.UID != client.UID)
                        {
                            dataList.Add(c.UID + ";" + c.PublicKey);
                        }
                    }
                    client.SendDataPacketToClient(new Packet(Packet.PacketType.ClientList, dataList));
                    break;
                case Packet.PacketType.Message:
                    Console.WriteLine("Входящее сообщение от " + p.uid + " в " + p.messageTimeStamp.ToString("HH:mm:ss") + " для " + p.destinationUID + " Сообщение: " + Encoding.UTF8.GetString(p.messageData));
                    foreach (ClientData c in clients)
                    {
                        if(c.UID == p.destinationUID)
                        {
                            c.SendDataPacketToClient(new Packet(Packet.PacketType.Message, p.messageTimeStamp, p.uid, p.destinationUID, p.messageData));
                            Console.WriteLine("Сообщение отпавлено " + c.UID);
                        }
                    }
                    break;
            }
        }


        private static ClientData GetClientFromList(TcpClient tcpClient)
        {
            foreach (ClientData client in clients)
            {
                if (client.TcpClient == tcpClient)
                {
                    return client;
                }
            }

            return null;
        }

        private static ClientData GetClientFromList(string uid)
        {
            foreach (ClientData client in clients)
            {
                if (client.UID == uid)
                {
                    return client;
                }
            }

            return null;
        }
    }
}
