using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Lib;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Runtime.InteropServices;
using System.IO;

namespace Client
{
    public partial class frm_main : Form
    {
        const int PORT = 1774;
        private TcpClient tcpClient;
        private NetworkStream clientStream;
        private string uid;
        private string publicKey;
        private string privateKey;

        public frm_main()
        {            
            InitializeComponent();

            Tuple<string, string> keyPair = KeyManagement.CreateKeyPair();
            privateKey = keyPair.Item1;
            publicKey = keyPair.Item2;
            MessageBox.Show("Private-Key:\n\n" + privateKey);

            tc_conversations.Padding = new Point(15, 4);
            tc_conversations.DrawMode = TabDrawMode.OwnerDrawFixed;

            tb_uid.Text = Properties.Settings.Default.UID;
            tb_ip.Text = Properties.Settings.Default.ServerIP;

            if (tb_uid.Text == "")
            {
                tb_uid.Text = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            }
        }

        private void btn_connect_Click(object sender, EventArgs e)
        {            
            if(tb_uid.Text == String.Empty || tb_ip.Text == String.Empty)
            {
                return;
            }

            uid = tb_uid.Text;

            ConnectToServer(tb_ip.Text, PORT);

            Properties.Settings.Default.UID = uid;
            Properties.Settings.Default.ServerIP = tb_ip.Text;
            Properties.Settings.Default.Save();            

            Thread receiveDataThread = new Thread(DataIn);
            receiveDataThread.Start();

            SendDataToServer(new Packet(Packet.PacketType.Registration, uid, publicKey));
            Log("Registration-Packet sent.\n");

            btn_connect.Enabled = false;
        }

        public void SendDataToServer(Packet packet)
        {
            byte[] packetBytes = packet.ConvertToBytes();

            var length = packetBytes.Length;
            var lengthBytes = BitConverter.GetBytes(length);
            clientStream.Write(lengthBytes, 0, 4); 
            clientStream.Write(packetBytes, 0, packetBytes.Length);   
        }

        private void DataManagerForIncommingServerPackets(Packet packet)
        {
            switch (packet.type)
            {               
                case Packet.PacketType.RegistrationSuccess:
                    Log("Регистрация прошла успешно.\n");
                    GetClientist();
                    break;
                case Packet.PacketType.RegistrationFail:
                    tcpClient.Close();
                    Log("Регистрация не удалась.");
                    MessageBox.Show("Регистрация не удалась!\n\nДетали:\n" + packet.singleStringData, "Регистрация не удалась!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    
                    break;
                case Packet.PacketType.ClientList:
                    Log("Список клиентов получен.");

                    InvokeGUIThread(() => {
                        foreach (object clientdata in packet.data)
                        {
                            string[] data = ((string) clientdata).Split(';');
                            lb_clients.Items.Add(new LocalClientData(data[0], data[1]));
                        }
                    });
                    break;
                case Packet.PacketType.ClientConnected:
                    Log("Подключился новый клиент.");

                    InvokeGUIThread(() => {                                                
                        string[] data = (packet.singleStringData).Split(';');
                        lb_clients.Items.Add(new LocalClientData(data[0], data[1]));                        
                    });
                    break;
                case Packet.PacketType.ClientDisconnected:
                    InvokeGUIThread(() => {

                        string[] data = (packet.singleStringData).Split(';');
                        string packetDataUID = data[0];
                        string packetDataPublicKey = data[1];

                        foreach (ConversationTabPage conversation in tc_conversations.TabPages)
                        {
                            if(conversation.UID == packetDataUID && conversation.PublicKey == packetDataPublicKey)
                            {
                                conversation.DisableAll("Клиент отключен.");
                            }
                        }

                        foreach (LocalClientData item in lb_clients.Items)
                        {
                            if(packetDataUID == item.uid && packetDataPublicKey == item.publicKey)
                            {
                                lb_clients.Items.Remove(item);
                                break;
                            }
                        }                        
                    });
                    break;
                case Packet.PacketType.Message:
                    Log("Входящее сообщение от " + packet.uid);
                    InvokeGUIThread(() => {
                        OnNewMessage(packet.uid, packet.messageTimeStamp, KeyManagement.Decrypt(privateKey, packet.messageData));
                    });
                    
                    break;
            }
        }

        private void OnNewMessage(string senderUID, DateTime timeStamp, string message)
        {            
            if (!this.Focused || this.WindowState == FormWindowState.Minimized)
            {
                FlashWindow.Start(this);               
            }

            ConversationTabPage userTab = TabExistsForUID(senderUID);

            if (userTab != null) 
            {
                userTab.NewMessageFromOther(senderUID, timeStamp, message);             
            }
            else
            {
                tc_conversations.TabPages.Add(new ConversationTabPage(this, senderUID, GetPublicKeyForUID(senderUID)));
                ConversationTabPage lastTP = (ConversationTabPage) tc_conversations.TabPages[tc_conversations.TabPages.Count - 1];
                Application.DoEvents();
                OnNewMessage(senderUID, timeStamp, message);                
            }           
        }

        private void ConnectToServer(string ip, int port)
        {
            tcpClient = new TcpClient();

            while (!tcpClient.Connected)
            {
                try
                {
                    Log("Попытка подключения к  " + ip + ":" + port + "...");
                    tcpClient.Connect(new IPEndPoint(IPAddress.Parse(ip), port));
                    clientStream = tcpClient.GetStream();
                    Log("Подключено");
                    MessageBox.Show("Подключение успешно!");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Сообщение: " + ex.Message);
                }
            }           
        }

        private void DataIn()
        {            
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

                    DataManagerForIncommingServerPackets(new Packet(buffer));
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Номер ошибки: " + ex.ErrorCode + " Сообщение: " + ex.Message);
            }
            catch (IOException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("Сервер отключён!");
                Console.ReadLine();
                Environment.Exit(0);
            }

        }

        private void InvokeGUIThread(Action action)
        {
            Invoke(action);
        }

        private void frm_main_FormClosing(object sender, FormClosingEventArgs e)
        {
            Environment.Exit(0);
        }

        public void GetClientist()
        {
            SendDataToServer(new Packet(Packet.PacketType.GetClientList));
        }

        private void lb_clients_DoubleClick(object sender, EventArgs e)
        {
            if (lb_clients.SelectedItem != null)
            {
                foreach (ConversationTabPage conversation  in tc_conversations.TabPages)
                {
                    if(conversation.UID == ((LocalClientData)lb_clients.SelectedItem).uid && conversation.PublicKey == ((LocalClientData)lb_clients.SelectedItem).publicKey)
                    {
                        Log(">> Беседа уже создана.");
                        tc_conversations.SelectedTab = conversation;
                        return;
                    }
                }

                tc_conversations.TabPages.Add(new ConversationTabPage(this, ((LocalClientData)lb_clients.SelectedItem).uid, ((LocalClientData)lb_clients.SelectedItem).publicKey));
                tc_conversations.SelectedTab = tc_conversations.TabPages[tc_conversations.TabPages.Count-1];
            }
        }

        public void Log(string message)
        {
            InvokeGUIThread(() => { tb_log.Text += ">> " + message + "\n"; tb_log.ScrollToCaret(); });
        }       
        
        public void SendMessage(string destinationID, byte[] encrypedMessage)
        {
            SendDataToServer(new Packet(Packet.PacketType.Message, DateTime.Now, uid, destinationID, encrypedMessage));
        }

        private void tc_conversations_DrawItem(object sender, DrawItemEventArgs e)
        {
            var tabPage = this.tc_conversations.TabPages[e.Index];
            var tabRect = this.tc_conversations.GetTabRect(e.Index);
            tabRect.Inflate(-2, -2);
            
            var closeImage = Properties.Resources.IconClose;
            e.Graphics.DrawImage(closeImage, (tabRect.Right - closeImage.Width), tabRect.Top + (tabRect.Height - closeImage.Height) / 2);
            TextRenderer.DrawText(e.Graphics, tabPage.Text, tabPage.Font, tabRect, tabPage.ForeColor, TextFormatFlags.Left);            
        }

        private void tc_conversations_MouseDown(object sender, MouseEventArgs e)
        {
            for (var i = 0; i < tc_conversations.TabPages.Count; i++)
            {
                var tabRect = tc_conversations.GetTabRect(i);
                tabRect.Inflate(-2, -2);
                var closeImage = Properties.Resources.IconClose;
                var imageRect = new Rectangle((tabRect.Right - closeImage.Width), tabRect.Top + (tabRect.Height - closeImage.Height) / 2, closeImage.Width, closeImage.Height);
                if (imageRect.Contains(e.Location))
                {
                    tc_conversations.TabPages.RemoveAt(i);
                    break;
                }
            }
        }

        private ConversationTabPage TabExistsForUID(string uid)
        {
            foreach (ConversationTabPage conversation in tc_conversations.TabPages)
            {
                if(conversation.UID == uid)
                {
                    return conversation;
                }
            }

            return null;
        }

        private bool TabIsActiveForUID(string uid)
        {
            ConversationTabPage currentTab = (ConversationTabPage) tc_conversations.SelectedTab;

            if (currentTab.UID == uid)
            {
                return true;
            }           

            return false;
        }        

        private string GetPublicKeyForUID(string uid)
        {
            foreach (LocalClientData client in lb_clients.Items)
            {
                if (client.uid == uid)
                {
                    return client.publicKey;
                }
            }

            throw new Exception("Клиент не существует!");
        }       

        private void tc_conversations_SelectedIndexChanged(object sender, EventArgs e)
        {
            ConversationTabPage currentTab = (ConversationTabPage) tc_conversations.SelectedTab;            
        }

        private void frm_main_Activated(object sender, EventArgs e)
        {
            FlashWindow.Stop(this);
        }

        private void btn_help_Click(object sender, EventArgs e)
        {
            Help.ShowHelp(this,"help.chm");
        }
    }
}
