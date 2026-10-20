using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace IpaDesktop
{
    /// <summary>Создаёт HTTP-клиенты с TLS и идентификатором приложения для запросов к источникам обновлений.</summary>
    public static class NetworkClient
    {
        public static readonly HttpClient Http = CreateHttp();
        /// <summary>Создаёт клиент с заданным временем ожидания и User-Agent текущей версии приложения.</summary>
        static HttpClient CreateHttp()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var h = new HttpClient();
            h.Timeout = TimeSpan.FromMinutes(15);
            h.DefaultRequestHeaders.UserAgent.ParseAdd("IPA-Desktop/" + AppInfo.Version);
            return h;
        }
    }
}
