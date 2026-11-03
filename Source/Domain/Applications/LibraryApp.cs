namespace IpaDesktop;

public sealed class LibraryApp
{
    public string Name { get; set; }
    public string ID { get; set; }
    public string BundleID { get; set; }
    public string Version { get; set; }
    public string Build { get; set; }
    public bool Placeholder { get; set; }
    public string Kind { get; set; }
    public string Date { get; set; }
    public string Status => Placeholder ? "Сгружено / placeholder" : Kind ?? "На устройстве";
    public bool Downloadable => AppSetStore.ValidId(ID) && (Kind == null || Kind.Contains("iOS",System.StringComparison.OrdinalIgnoreCase) || Kind.Contains("software",System.StringComparison.OrdinalIgnoreCase));
}
