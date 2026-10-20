namespace IpaDesktop
{
    /// <summary>Ответ GitHub Releases с тегом и архивами; используется при проверке доступных обновлений.</summary>
    public class Release
    {
        public bool draft { get; set; }
        public bool prerelease { get; set; }
        public string tag_name
        {
            get;
            set;
        }
        public string html_url
        {
            get;
            set;
        }
        public string body
        {
            get;
            set;
        }
        public Asset[] assets
        {
            get;
            set;
        }
    }
}
