using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.IO;
using System.Threading;

namespace InstantMessengerServer
{
    public class Client
    {
        public Client(Program p, TcpClient c)
        {
            prog = p;
            client = c;

            // İstemciyi başka bir iş parçacığında ele alın.
            (new Thread(new ThreadStart(SetupConn))).Start();
        }

        Program prog;
        public TcpClient client;
        public NetworkStream netStream;  // Bağlantının ham veri akışı.
        public SslStream ssl;            // şifrelenmıs baglantı ssl ile
        public BinaryReader br;
        public BinaryWriter bw;

        UserInfo userInfo;  // var olan kullancılar hakkında bilgi.
        
        void SetupConn() 
        {
            try
            {
                Console.WriteLine("[{0}] New connection!", DateTime.Now);
                netStream = client.GetStream();
                ssl = new SslStream(netStream, false);
                ssl.AuthenticateAsServer(prog.cert, false, SslProtocols.Tls, true);
                Console.WriteLine("[{0}] Connection authenticated!", DateTime.Now);
                // şimdi şifrelenmiş baglantıyı elde ettık

                br = new BinaryReader(ssl, Encoding.UTF8);
                bw = new BinaryWriter(ssl, Encoding.UTF8);

                // merhaba
                bw.Write(IM_Hello);
                bw.Flush();
                int hello = br.ReadInt32();
                if (hello == IM_Hello)
                {
                    
                    byte logMode = br.ReadByte();
                    string userName = br.ReadString();
                    string password = br.ReadString();
                    if (userName.Length < 10) 
                    {
                        if (password.Length < 20) 
                        {
                            if (logMode == IM_Register) 
                            {
                                if (!prog.users.ContainsKey(userName))  
                                {
                                    userInfo = new UserInfo(userName, password, this);
                                    prog.users.Add(userName, userInfo);  
                                    bw.Write(IM_OK);
                                    bw.Flush();
                                    Console.WriteLine("[{0}] ({1}) Registered new user", DateTime.Now, userName);
                                    prog.SaveUsers();
                                    Receiver();  
                                }
                                else
                                    bw.Write(IM_Exists);
                            }
                            else if (logMode == IM_Login)  
                            {
                                if (prog.users.TryGetValue(userName, out userInfo))  
                                {
                                    if (password == userInfo.Password)  
                                    {
                                        
                                        if (userInfo.LoggedIn)
                                            userInfo.Connection.CloseConn();

                                        userInfo.Connection = this;
                                        bw.Write(IM_OK);
                                        bw.Flush();
                                        Receiver();  
                                    }
                                    else
                                        bw.Write(IM_WrongPass);
                                }
                                else
                                    bw.Write(IM_NoExists);
                            }
                        }
                        else
                            bw.Write(IM_TooPassword);
                    }
                    else
                        bw.Write(IM_TooUsername);
                }
                CloseConn();
            }
            catch { CloseConn(); }
        }
        void CloseConn() 
        {
            try
            {
                userInfo.LoggedIn = false;
                br.Close();
                bw.Close();
                ssl.Close();
                netStream.Close();
                client.Close();
                Console.WriteLine("[{0}] End of connection!", DateTime.Now);
            }
            catch { }
        }
        void Receiver()  
        {
            Console.WriteLine("[{0}] ({1}) User logged in", DateTime.Now, userInfo.UserName);
            userInfo.LoggedIn = true;

            try
            {
                while (client.Client.Connected)  
                {
                    byte type = br.ReadByte();  

                    if (type == IM_IsAvailable)
                    {
                        string who = br.ReadString();

                        bw.Write(IM_IsAvailable);
                        bw.Write(who);

                        UserInfo info;
                        if (prog.users.TryGetValue(who, out info))
                        {
                            if (info.LoggedIn)
                                bw.Write(true);   
                            else
                                bw.Write(false);  
                        }
                        else
                            bw.Write(false);      
                        bw.Flush();
                    }
                    else if (type == IM_Send)
                    {
                        string to = br.ReadString();
                        string msg = br.ReadString();

                        UserInfo recipient;
                        if (prog.users.TryGetValue(to, out recipient))
                        {
                           
                            if (recipient.LoggedIn)
                            {
                                
                                recipient.Connection.bw.Write(IM_Received);
                                recipient.Connection.bw.Write(userInfo.UserName);
                                recipient.Connection.bw.Write(msg);
                                recipient.Connection.bw.Flush();
                                Console.WriteLine("[{0}] ({1} -> {2}) Message sent!", DateTime.Now, userInfo.UserName, recipient.UserName);
                            }
                        }
                    }
                }
            }
            catch (IOException) { }

            userInfo.LoggedIn = false;
            Console.WriteLine("[{0}] ({1}) User logged out", DateTime.Now, userInfo.UserName);
        }

        public const int IM_Hello = 2012;      
        public const byte IM_OK = 0;           
        public const byte IM_Login = 1;        
        public const byte IM_Register = 2;     
        public const byte IM_TooUsername = 3;  
        public const byte IM_TooPassword = 4;  
        public const byte IM_Exists = 5;       
        public const byte IM_NoExists = 6;     
        public const byte IM_WrongPass = 7;    
        public const byte IM_IsAvailable = 8;  
        public const byte IM_Send = 9;         
        public const byte IM_Received = 10;    
    }
}
