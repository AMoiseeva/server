using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Threading;
using System.IO;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace server
{
    class Server
    {
        static void ClientThread(Object StateInfo)
        {
            new Client((TcpClient)StateInfo);
        }

        TcpListener Listener;
        // Запуск сервера
        public Server(int Port)
        {
            // Создаем "слушателя" для указанного порта
            Listener = new TcpListener(IPAddress.Any, Port);
            Listener.Start(); // Запускаем его

            // В бесконечном цикле
            while (true)
            {
                // Принимаем нового клиента
                TcpClient Client = Listener.AcceptTcpClient();
                // Создаем поток
                Thread Thread = new Thread(new ParameterizedThreadStart(ClientThread));
                // И запускаем этот поток, передавая ему принятого клиента
                Thread.Start(Client);
            }
        }

        // Остановка сервера
        ~Server()
        {
            // Если "слушатель" был создан
            if (Listener != null)
            {
                // Остановим его
                Listener.Stop();
            }
        }
        static void Main(string[] args)
        {
            // Создадим новый сервер на порту 80
            new Server(100);
        }
    }
    class Client
    {
        private Responce SendError(int Code)
        {
            string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            string Html = "<html><body><h1>" + CodeStr + "</h1></body></html>";
            Responce response = new Responce();
            response.Version = "HTTP/1.1";
            response.Status = CodeStr;
            response.Headers = new Dictionary<string, string>();
            response.Headers.Add("Content-type:", "text/html");
            response.Headers.Add("Content-Length:", Html.Length.ToString());
            response.Body = Html;
            return response;
        }

        string Bilder(Responce response)
        {
            string str = response.Version + ' ' + response.Status + '\n';
            foreach (var i in response.Headers)
            {
                str += i.Key + ": " + i.Value + '\n';
            }
            str += '\n' + response.Body;
            return str;
        }

        //parsing
        Request parsing(string strR)
        {
            Request reqR = new Request();
            string[] words = strR.Split('\n');
            string[] first = words[0].Split(' ');
            reqR.Method = first[0];
            reqR.Path = first[1];
            reqR.Version = first[2];
            reqR.Headers = new Dictionary<string, string>();
            int i = 1;
            while (words[i] != "\r")
            {
                string[] headers = words[i].Split(' ');
                string body = "";
                for (int j=1;j<headers.Count();j++)
                {
                    body += headers[j]+" ";
                }
                reqR.Headers.Add(headers[0],body);
                i++;
            }
            reqR.Body = "";
            while (i < words.Count())
            {
                reqR.Body = reqR.Body + words[i];
                i++;
            }
            return reqR;
        }

        void Response(Responce response, TcpClient Client)
        {
            string Str = Bilder(response);
            byte[] Buffer = Encoding.ASCII.GetBytes(Str);
            Client.GetStream().Write(Buffer, 0, Buffer.Length);
            if (response.FilePath != null)
            {
                FileStream FS = new FileStream(response.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                while (FS.Position < FS.Length)
                {
                    // Читаем данные из файла
                    var Count = FS.Read(Buffer, 0, Buffer.Length);
                    // И передаем их клиенту
                    Client.GetStream().Write(Buffer, 0, Count);
                }
                FS.Close();
            }
            Client.Close();
        }

        void Response2(Responce response, TcpClient Client,FileStream FS)
        {
            string Str = Bilder(response);
            byte[] Buffer1 = Encoding.ASCII.GetBytes(Str);
            Client.GetStream().Write(Buffer1, 0, Buffer1.Length);
            Client.Close();
        }

        public Responce ProcessRequest(Request request, IAuthorization authorizator)
        {

            if (!authorizator.CheckIdentification(request))
                return authorizator.GetFailedResponce();
            else
            {
                if (!authorizator.CheckAuthorization(request))
                    return SendError(403);
            }

            string RequestUri = request.Path;
            RequestUri = Uri.UnescapeDataString(RequestUri);
                
            if (RequestUri.IndexOf("..") >= 0)
            {
                return SendError(400);
            }
            
            if (RequestUri.EndsWith("/"))
            {
                RequestUri += "index.html";
            }
            string FilePath = Environment.CurrentDirectory + RequestUri;
                
            if (!File.Exists(FilePath))
            {
                Console.WriteLine(FilePath);
                return SendError(404);
            }
            
            string Extension = RequestUri.Substring(RequestUri.LastIndexOf('.'));
                
            string ContentType = "";
                
            switch (Extension)
            {
                case ".htm":
                case ".html":
                    ContentType = "text/html";
                    break;
                case ".css":
                    ContentType = "text/stylesheet";
                    break;
                case ".js":
                    ContentType = "text/javascript";
                    break;
                case ".jpg":
                    ContentType = "image/jpeg";
                    break;
                case ".jpeg":
                case ".png":
                case ".gif":
                    ContentType = "image/" + Extension.Substring(1);
                    break;
                default:
                    if (Extension.Length > 1)
                    {
                        ContentType = "application/" + Extension.Substring(1);
                    }
                    else
                    {
                        ContentType = "application/unknown";
                    }
                    break;
            }

            // Открываем файл, страхуясь на случай ошибки
            FileStream FS;
            try
            {
                FS = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception)
            {
                // Если случилась ошибка, посылаем клиенту ошибку 500
                return SendError(500);
            }
            
            Responce response = new Responce();
            response.Version = "HTTP/1.1";
            response.Status = "200 OK";
            response.Headers = new Dictionary<string, string>();
            response.Headers.Add("Content-type:", ContentType);
            response.Headers.Add("Content-Length:", FS.Length.ToString());
            response.FilePath = FilePath;
            FS.Close();
            return response;
        }

        public Client(TcpClient Client)
        {
            string Request = "";
            byte[] Buffer = new byte[1024];
            int Count;
            while ((Count = Client.GetStream().Read(Buffer, 0, Buffer.Length)) > 0)
            {
                Request += Encoding.ASCII.GetString(Buffer, 0, Count);
                if (Request.IndexOf("\r\n\r\n") >= 0 || Request.Length > 4096)
                {
                    break;
                }
            }

            Responce response = new Responce();

            if (Request == "")
            {
                response = SendError(400);
                return;
            }
            Request ReqClient = parsing(Request);

            response = ProcessRequest(ReqClient, new DigestAuthentication());
            Response(response, Client);
            return;

        }
    }

    public class Request
    {
        public string Method { get; set; }

        public string Path { get; set; }

        public string Version { get; set; }

        public Dictionary<string,string> Headers { get; set; }

        public string Body { get; set; }
    }

    public class Responce
    {
        public string Version { get; set; }

        public string Status { get; set; }

        public Dictionary<string, string> Headers { get; set; }

        public string Body { get; set; }

        public string FilePath { get; set; }
    }

    public interface IAuthorization
    {
        bool CheckIdentification(Request request);

        Responce GetFailedResponce();

        bool CheckAuthorization(Request request);
    }

    public class BasicAuthentication : IAuthorization
    {
        public bool CheckIdentification(Request request)
        {
            if (request.Headers.ContainsKey("Authorization:"))
            {
                return true;
            }
            else return false;
        }

        public Responce GetFailedResponce()
        {
            int Code = 401;
            string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            string Html = "<html><body><h1>" + CodeStr + "</h1></body></html>";
            Responce response = new Responce();
            response.Version = "HTTP/1.1";
            response.Status = CodeStr;
            response.Headers = new Dictionary<string, string>();
            response.Headers.Add("WWW-Authenticate", "Basic realm=\"My Server\"");
            response.Headers.Add("Content-type", "text/html");
            response.Headers.Add("Content-Length", Html.Length.ToString());
            response.Body = Html;
            return response;
        }

        public bool CheckAuthorization(Request request)
        {
            string logB;
            request.Headers.TryGetValue("Authorization:", out logB);
            string[] sym = logB.Split(' ');
            string loginB = sym[1];
            string[] sym1 = loginB.Split('\r');
            byte[] newBytes = Convert.FromBase64String(sym1[0]);
            string login = Encoding.UTF8.GetString(newBytes);
            string[] AT = login.Split(':');
            if ((AT[0] == "AAA") && (AT[1] == "AAA"))
            {
                return true;
            }
            else return false;
        }
    }

    public class DigestAuthentication : IAuthorization
    {
        public bool CheckIdentification(Request request)
        {
            if (request.Headers.ContainsKey("Authorization:"))
            {
                return true;
            }
            else return false;
        }

        public Responce GetFailedResponce()
        {
            int Code = 401;
            string CodeStr = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            string Html = "<html><body><h1>" + CodeStr + "</h1></body></html>";
            Responce response = new Responce();
            response.Version = "HTTP/1.1";
            response.Status = CodeStr;
            response.Headers = new Dictionary<string, string>();
            response.Headers.Add("WWW-Authenticate", "Digest realm=\"MyServer\", nonce = \"dcd98b7102dd2f0e8b11d0f600bfb0c093\", opaque = \"5ccc069c403ebaf9f0171e9517f40e41\"");
            response.Headers.Add("Content-type", "text/html");
            response.Headers.Add("Content-Length", Html.Length.ToString());
            response.Body = Html;
            return response;
        }


        public bool CheckAuthorization(Request request)
        {

            string components;
            request.Headers.TryGetValue("Authorization:", out components);
            char[] simvols = { ',', ' ', '=', '"', '\r' };
            string[] sym = components.Split(simvols);
            string username = sym[3];
            string realm = sym[8];
            string nonce = sym[13];
            string uri = sym[18];
            string response = sym[23];
            string opaque = sym[28];

            MD5 md5Hash = MD5.Create();
            byte[] ha1 = md5Hash.ComputeHash(Encoding.UTF8.GetBytes("AAA:" + realm + ":AAA"));
            byte[] ha2 = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(request.Method + ":" + uri));
            StringBuilder sha1 = new StringBuilder();
            for (int i = 0; i < ha1.Length; i++)
            {
                sha1.Append(ha1[i].ToString("x2"));
            }
            StringBuilder sha2 = new StringBuilder();
            for (int i = 0; i < ha2.Length; i++)
            {
                sha2.Append(ha2[i].ToString("x2"));
            }
            byte[] MD = md5Hash.ComputeHash(Encoding.UTF8.GetBytes(sha1.ToString()+':' + nonce+':' + sha2.ToString()));
            StringBuilder smd = new StringBuilder();
            for (int i = 0; i < MD.Length; i++)
            {
                smd.Append(MD[i].ToString("x2"));
            }
            Console.WriteLine(response);
            Console.WriteLine(smd.ToString());
            if (response == smd.ToString())
            {
                return true;
            }
        
            else return false;
        }
    }


}
