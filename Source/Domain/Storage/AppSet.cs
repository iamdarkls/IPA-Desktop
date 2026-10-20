using System;
namespace IpaDesktop
{
    /// <summary>Сохранённый набор ID; Files используется только для чтения и миграции прежнего формата.</summary>
    public sealed class AppSet
    {
        public string Name { get; set; }
        public AppRow[] Apps { get; set; }
        // Read-only compatibility with sets saved before 0.1.18.
        public string[] Files { get; set; }
        public string Label { get { return Name + " · " + (Apps == null ? (Files == null ? 0 : Files.Length) : Apps.Length) + " приложений" + (Apps == null ? " · прежний формат" : ""); } }
    }
}
