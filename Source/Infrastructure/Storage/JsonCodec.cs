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
    /// <summary>Единая обёртка JSON-сериализации для совместимости с .NET Framework без внешних пакетов.</summary>
    public static class JsonCodec
    {
        static JavaScriptSerializer Serializer
        {
            get
            {
                return new JavaScriptSerializer
                {
                    MaxJsonLength = 16000000
                };
            }
        }
        public static string Serialize(object value)
        {
            return Serializer.Serialize(value);
        }
        public static T Deserialize<T> (string value)
        {
            return Serializer.Deserialize<T> (value);
        }
        public static object DeserializeObject(string value)
        {
            return Serializer.DeserializeObject(value);
        }
    }
}
