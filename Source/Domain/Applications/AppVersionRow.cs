namespace IpaDesktop
{
    /// <summary>Строка таблицы версий; неизвестные поля не подменяются данными другой версии.</summary>
    public sealed class AppVersionRow
    {
        public string ID { get; set; }
        public string Version { get; set; }
        public string MinimumOS { get; set; }
        public string Platforms { get; set; }
        public string State { get; set; }
        public AppVersionRow() { Version = "—"; MinimumOS = "—"; Platforms = "—"; State = "Не запрошено"; }
    }
}
