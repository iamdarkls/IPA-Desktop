namespace IpaDesktop
{
    /// <summary>Описание архива GitHub Releases; имена полей сохраняют контракт JSON API.</summary>
    public class Asset
    {
        public string name
        {
            get;
            set;
        }
        public string browser_download_url
        {
            get;
            set;
        }
        public string digest
        {
            get;
            set;
        }
        public long size
        {
            get;
            set;
        }
    }
}
