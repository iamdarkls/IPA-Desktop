namespace IpaDesktop
{
    /// <summary>Снимок запроса загрузки с необязательным ID версии; null означает последнюю доступную.</summary>
    public class DownloadItem
    {
        public string Name
        {
            get;
            set;
        }
        public string ID
        {
            get;
            set;
        }
        public string VersionID
        {
            get;
            set;
        }
        public string Key { get { return ID + "@" + (VersionID ?? "latest"); } }
        public string Label
        {
            get
            {
                return Name + (VersionID == null ? " · последняя" : " · версия " + VersionID);
            }
        }
    }
}
