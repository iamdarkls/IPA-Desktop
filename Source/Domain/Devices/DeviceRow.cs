namespace IpaDesktop
{
    /// <summary>Обнаруженный iPhone: подпись для интерфейса и реальный UDID для команд установки.</summary>
    public class DeviceRow
    {
        public string ID
        {
            get;
            set;
        }
        public string Name
        {
            get;
            set;
        }
        public bool Network { get; set; }
        public string Connection { get { return Network ? "Wi-Fi" : "USB"; } }
        public string DisplayLabel { get { return Name + " · " + Connection; } }
        public override string ToString()
        {
            return Name;
        }
    }
}
