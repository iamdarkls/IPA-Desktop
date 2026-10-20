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
    /// <summary>Загружает state.json и сохраняет его атомарно; используется сервисами установки и отката.</summary>
    public sealed class SettingsStore
    {
        public string Root
        {
            get;
            private set;
        }
        public State Settings
        {
            get;
            set;
        }
        public SettingsStore(string root)
        {
            Root = root;
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "state.json");
            Settings = File.Exists(path) ? JsonCodec.Deserialize<State> (File.ReadAllText(path)) : new State();
            if (Settings == null) throw new IOException("Не удалось прочитать настройки.");
        }
        /// <summary>Атомарно сохраняет текущие настройки без частичного изменения существующего файла.</summary>
        public void Save()
        {
            AtomicFile.WriteUtf8(Path.Combine(Root, "state.json"), JsonCodec.Serialize(Settings));
        }
    }
}
