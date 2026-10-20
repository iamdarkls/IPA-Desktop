namespace IpaDesktop
{
    /// <summary>Метаданные IPA, прочитанные из архива; отсутствующие поля остаются неизвестными.</summary>
    public sealed class IpaDetails
    {
        public string AppStoreID { get; set; }
        public string Account { get; set; }
        public string BundleID { get; set; }
        public string AppName { get; set; }
        public string Version { get; set; }
        public string MinimumOS { get; set; }
        public string[] Platforms { get; set; }
        public string Build { get; set; }
    }
}
