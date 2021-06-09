﻿using Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Server
{
    class ClientData
    {
        private TcpClient _client;
        private Thread _clientThread;
        private NetworkStream _clientStream;
        private string _publicKey;
        private string _uid;

        public TcpClient TcpClient { get => _client; set => _client = value; }       
        public string UID { get => _uid; set => _uid = value; }
        public string PublicKey { get => _publicKey; set => _publicKey = value; }

        public ClientData(TcpClient client)
        {
            this._client = client;
            this._clientStream = client.GetStream();
            _clientThread = new Thread(Server.DataIn);
            _clientThread.Start(client);

            _publicKey = "";
            _uid = "";
        }

        public void SendDataPacketToClient(Packet packet)
        {
            byte[] packetBytes = packet.ConvertToBytes();

            var length = packetBytes.Length;
            var lengthBytes = BitConverter.GetBytes(length);
            _clientStream.Write(lengthBytes, 0, 4);
            _clientStream.Write(packetBytes, 0, packetBytes.Length); 
        }
    }
}
