using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.IO;
using System.Security.Cryptography;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace InstantMessenger
{
    public class IMClient
    {
        Thread tcpThread;      // Alıcı
        bool _con = false;    // bağlantı sağlandı mı sağlanmadı mı
        bool _logged = false;  // giriş yaptı mı
        string _user;          // kullanıcı adı
        string _pass;          // şifre
        bool reg;              // kayıt olma

        public string Server { get { return "localhost"; } }  // Sunucunun adresi. Bu durumda - yerel IP adresi.
        public int Port { get { return 2000; } }

        public bool IsLoggedIn { get { return _logged; } }
        public string UserName { get { return _user; } }
        public string Password { get { return _pass; } }

        // Bağlantı dizisini başlatın ve oturum açın veya kaydolun.
        void connect(string user, string password, bool register)
        {
            if (!_con)
            {
                _con = true;
                _user = user;
                _pass = password;
                reg = register;
                tcpThread = new Thread(new ThreadStart(SetupConn));
                tcpThread.Start();
            }
        }
        public void Login(string user, string password)
        {
            connect(user, password, false);
        }
        public void Register(string user, string password)
        {
            connect(user, password, true);
        }
        public void Disconnect()
        {
            if (_con)
                CloseConn();
        }

        public void IsAvailable(string user)
        {
            if (_con)
            {
                bw.Write(IM_IsAvailable);
                bw.Write(user);
                bw.Flush();
            }
        }
        public void SendMessage(string to, string msg)
        {
            if (_con)
            {
                bw.Write(IM_Send);
                bw.Write(to);
                bw.Write(msg);
                bw.Flush();
            }
        }

        // Olaylar
        public event EventHandler LoginOK;
        public event EventHandler RegisterOK;
        public event IMErrorEventHandler LoginFailed;
        public event IMErrorEventHandler RegisterFailed;
        public event EventHandler Disconnected;
        public event IMAvailEventHandler UserAvailable;
        public event IMReceivedEventHandler MessageReceived;
        
        virtual protected void OnLoginOK()
        {
            if (LoginOK != null)
                LoginOK(this, EventArgs.Empty);
        }
        virtual protected void OnRegisterOK()
        {
            if (RegisterOK != null)
                RegisterOK(this, EventArgs.Empty);
        }
        virtual protected void OnLoginFailed(IMErrorEventArgs e)
        {
            if (LoginFailed != null)
                LoginFailed(this, e);
        }
        virtual protected void OnRegisterFailed(IMErrorEventArgs e)
        {
            if (RegisterFailed != null)
                RegisterFailed(this, e);
        }
        virtual protected void OnDisconnected()
        {
            if (Disconnected != null)
                Disconnected(this, EventArgs.Empty);
        }
        virtual protected void OnUserAvail(IMAvailEventArgs e)
        {
            if (UserAvailable != null)
                UserAvailable(this, e);
        }
        virtual protected void OnMessageReceived(IMReceivedEventArgs e)
        {
            if (MessageReceived != null)
                MessageReceived(this, e);
        }

        
        TcpClient client;
        NetworkStream netStream;
        SslStream ssl;
        BinaryReader br;
        BinaryWriter bw;

        void SetupConn()  //Bağlantı kurma ve oturum açma 
        {
            client = new TcpClient(Server, Port);  // servere bağlanma
            netStream = client.GetStream();
            ssl = new SslStream(netStream, false, new RemoteCertificateValidationCallback(ValidateCert));
            ssl.AuthenticateAsClient("InstantMessengerServer");
            // Şimdi şifrelenmiş bir baglantı saglamıs olduk

            br = new BinaryReader(ssl, Encoding.UTF8);
            bw = new BinaryWriter(ssl, Encoding.UTF8);

            // Alıcı 'merhaba'
            int hello = br.ReadInt32();
            if (hello == IM_Hello)
            {
                // Merhaba ve cevap
                bw.Write(IM_Hello);

                bw.Write(reg ? IM_Register : IM_Login);  // giriş yada kayıt
                bw.Write(UserName);
                bw.Write(Password);
                bw.Flush();

                byte ans = br.ReadByte();  // cevabı oku
                if (ans == IM_OK)  // giriş/kayıt
                {
                    if (reg)
                        OnRegisterOK();  // kayıt tamam
                    OnLoginOK();  // giriş(eğer kayıt yapıldıysa otomatık giriş yapar)
                    Receiver(); // gelıcek mesajı dınleme
                }
                else
                {
                    IMErrorEventArgs err = new IMErrorEventArgs((IMError)ans);
                    if (reg)
                        OnRegisterFailed(err);
                    else
                        OnLoginFailed(err);
                }
            }
            if (_con)
                CloseConn();
        }
        void CloseConn() // baglantının sonlanması
        {
            br.Close();
            bw.Close();
            ssl.Close();
            netStream.Close();
            client.Close();
            OnDisconnected();
            _con = false;
        }
        void Receiver()  // butun gelen paketlerı al
        {
            _logged = true;

            try
            {
                while (client.Connected)  // biz bağlandıgımızda
                {
                    byte type = br.ReadByte();  // gelen paketi al

                    if (type == IM_IsAvailable)
                    {
                        string user = br.ReadString();
                        bool isAvail = br.ReadBoolean();
                        OnUserAvail(new IMAvailEventArgs(user, isAvail));
                    }
                    else if (type == IM_Received)
                    {
                        string from = br.ReadString();
                        string msg = br.ReadString();
                        OnMessageReceived(new IMReceivedEventArgs(from, msg));
                    }
                }
            }
            catch (IOException) { }

            _logged = false;
        }

        // Packet types
        public const int IM_Hello = 2012;      // merhaba
        public const byte IM_OK = 0;           // tamam
        public const byte IM_Login = 1;        // giriş
        public const byte IM_Register = 2;     // kayıt
        public const byte IM_TooUsername = 3;  // cok uzun kullanıcı adı
        public const byte IM_TooPassword = 4;  // cok uzun sıfre
        public const byte IM_Exists = 5;       // zaten var
        public const byte IM_NoExists = 6;     // böyle bır kayıt yok
        public const byte IM_WrongPass = 7;    // yanlıs sıfre
        public const byte IM_IsAvailable = 8;  // bu kullanıcı var mı
        public const byte IM_Send = 9;         // mesaj yolla
        public const byte IM_Received = 10;    // mesaj alındı
        
        public static bool ValidateCert(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true; // Güvenilmeyen sertifikalara izin verin.
        }
    }
}
